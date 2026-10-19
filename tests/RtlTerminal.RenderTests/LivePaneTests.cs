using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using RtlTerminal;

internal static partial class Program
{
    private static void CheckLivePaneSessions()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var window = new MainWindow();
        var tabs = (IList)typeof(MainWindow).GetField("_tabs", flags)!.GetValue(window)!;
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethods(flags | BindingFlags.Static)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(window,args);
        object? Get(object o,string p) => o.GetType().GetProperty(p)!.GetValue(o);
        void Set(object o,string p,object? v) => o.GetType().GetProperty(p)!.SetValue(o,v);
        var tabType = typeof(MainWindow).GetNestedType("TerminalTab",BindingFlags.NonPublic)!;
        var profileType = typeof(MainWindow).GetNestedType("TerminalProfile",BindingFlags.NonPublic)!;
        var sessions = new List<ConPtySession>();
        var readers = new List<Task>();
        var root = (FrameworkElement)window.Content;
        void Pump()
        {
            root.Measure(new Size(980,620)); root.Arrange(new Rect(0,0,980,620)); root.UpdateLayout();
            root.Dispatcher.Invoke(() => {},DispatcherPriority.ContextIdle);
        }
        try
        {
            var panes = new List<object>();
            for(var i=0;i<2;i++)
            {
                var pane=Call("CreatePane",Enum.ToObject(profileType,0))!;
                var tab=Activator.CreateInstance(tabType,pane)!; tabs.Add(tab); panes.Add(pane);
                Call("ShowTab",tab); Pump();
                var session=new ConPtySession(80,20); sessions.Add(session);
                Set(pane,"Session",session); Set(pane,"Buffer",new TerminalBuffer(80,20));
                Set(pane,"CancellationTokenSource",new CancellationTokenSource());
                session.Start("dotnet \"" + typeof(ConPtyPersianProbe).Assembly.Location + "\" --conpty-child --shell-launch",Environment.CurrentDirectory);
                readers.Add(Task.Run(()=>Call("ReadOutputLoop",pane)));
            }
            string Text(object pane) => string.Concat(((TerminalBuffer)Get(pane,"Buffer")!).CaptureSnapshot().Lines.SelectMany(l=>l.Runs).Select(r=>r.Text));
            void WaitFor(Func<bool> condition)
            {
                var watch=Stopwatch.StartNew();
                while(!condition())
                {
                    Pump(); Thread.Sleep(15);
                    if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("Live pane output timed out");
                }
                Pump();
            }
            sessions[0].Write("echo ALPHA_BEFORE_JOIN\r"); sessions[1].Write("echo BETA_BEFORE_JOIN\r");
            WaitFor(()=>Text(panes[0]).Contains("ALPHA_BEFORE_JOIN")&&Text(panes[1]).Contains("BETA_BEFORE_JOIN"));
            if(!(bool)Call("TryJoinTab",tabs[0],false)!)throw new Exception("Live panes could not be joined");
            sessions[0].Write("echo ALPHA_AFTER_JOIN\r"); sessions[1].Write("echo BETA_AFTER_JOIN\r");
            WaitFor(()=>Text(panes[0]).Contains("ALPHA_AFTER_JOIN")&&Text(panes[1]).Contains("BETA_AFTER_JOIN"));
            if(Text(panes[0]).Contains("BETA_AFTER_JOIN")||Text(panes[1]).Contains("ALPHA_AFTER_JOIN") ||
               !ReferenceEquals(Get(panes[0],"Session"),sessions[0]) || !ReferenceEquals(Get(panes[1],"Session"),sessions[1]))
                throw new Exception("Joining crossed shell input/output or replaced a running session");
            var targetView=(TerminalView)Get(panes[0],"View")!;
            if(!ReferenceEquals(Call("FindDropPane",targetView),panes[0]))throw new Exception("Drop hit testing selected the wrong pane");
            var drop=new DataObject(DataFormats.UnicodeText,"echo DROP_TARGET_ONLY\r");
            ((Task)Call("DropIntoPaneAsync",panes[0],drop)!).GetAwaiter().GetResult();
            WaitFor(()=>Text(panes[0]).Contains("DROP_TARGET_ONLY"));
            if(Text(panes[1]).Contains("DROP_TARGET_ONLY"))throw new Exception("A drop was sent to the active pane instead of its target");
            var path=Path.Combine(Environment.CurrentDirectory,"test & file %RTL_DROP_TOKEN%!.png");
            sessions[0].Write("echo ");
            ((Task)Call("DropIntoPaneAsync",panes[0],new DataObject(DataFormats.FileDrop,new[]{path}))!).GetAwaiter().GetResult();
            sessions[0].Write("\r");WaitFor(()=>Text(panes[0]).Contains("%RTL_DROP_TOKEN%!"));
            var image=System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);
            sessions[0].Write("echo ");
            ((Task)Call("DropIntoPaneAsync",panes[0],new DataObject(DataFormats.Bitmap,image))!).GetAwaiter().GetResult();
            sessions[0].Write("\r");WaitFor(()=>Text(panes[0]).Contains("clipboard-"));
            var images=(List<string>)typeof(MainWindow).GetField("_temporaryClipboardFiles",flags)!.GetValue(window)!;
            if(images.Count!=1 || !File.Exists(images[0]))throw new Exception("Dropped image was not saved as a temporary PNG");
            CheckUnexpectedShellEnd(Call, tabs, tabType, profileType, Set, Get, Pump, Text);
            Console.WriteLine("PASS two live shells, target-pane text/file/image drops, literal paths and temporary image creation");
        }
        finally
        {
            // Keep readers draining while each native host shuts down.
            foreach(var pane in tabs.Cast<object>().SelectMany(t=>((IList)Get(t,"Panes")!).Cast<object>())) Set(pane,"Session",null);
            Task.WaitAll(sessions.Select(s=>Task.Run(s.Dispose)).ToArray(),TimeSpan.FromSeconds(10));
            window.Close();
            Task.WaitAll(readers.ToArray(),TimeSpan.FromSeconds(10));
        }
    }

    private static void CheckUnexpectedShellEnd(Func<string, object?[], object?> call, IList tabs,
        Type tabType, Type profileType, Action<object, string, object?> set, Func<object, string, object?> get,
        Action pump, Func<object, string> text)
    {
        var pane = call("CreatePane", [Enum.ToObject(profileType, 0)])!;
        var tab = Activator.CreateInstance(tabType, pane)!;
        tabs.Add(tab);
        call("ShowTab", [tab]);
        pump();
        set(pane, "Buffer", new TerminalBuffer(80, 20));
        set(pane, "CancellationTokenSource", new CancellationTokenSource());
        var eof = new FakeTransport(false);
        set(pane, "Session", eof);
        call("ReadOutputLoop", [pane]);
        if (!text(pane).Contains("[Shell exited", StringComparison.Ordinal))
            throw new Exception("Unexpected shell EOF was not shown in the terminal");

        set(pane, "Buffer", new TerminalBuffer(80, 20));
        set(pane, "CancellationTokenSource", new CancellationTokenSource());
        var failed = new FakeTransport(true);
        set(pane, "Session", failed);
        call("ReadOutputLoop", [pane]);
        if (!text(pane).Contains("[Terminal output error:", StringComparison.Ordinal))
            throw new Exception("Terminal read failure was not shown in the terminal");
        set(pane, "Session", null);
        eof.Dispose(); failed.Dispose();
        Console.WriteLine("PASS unexpected shell exit and output errors are visible in the terminal");
    }

    private sealed class FakeTransport(bool fail) : ITerminalTransport
    {
        public bool HasCodexProcess => false;
        public int Read(byte[] buffer) => fail ? throw new IOException("probe failure") : 0;
        public void Write(string text) { }
        public void Resize(short columns, short rows) { }
        public void Dispose() { }
    }
}
