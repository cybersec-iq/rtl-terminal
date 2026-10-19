const assert = require("node:assert/strict");
const { test } = require("node:test");
const {
  parseSgrMouseData, menuViewport, menuItemAtRow, buildMenuHeader,
  classifySgrMouseReport, nextMouseSelection, mouseTrackingSequence,
} = require("../../AgentEngine/xagent.js");

test("SGR mouse parser handles split packets, releases, wheel and multiple reports", () => {
  const first = parseSgrMouseData("", "prefix\x1b[<0;12;");
  assert.deepEqual(first.reports, []);
  assert.equal(first.pending, "\x1b[<0;12;");

  const second = parseSgrMouseData(first.pending, "7M\x1b[<0;12;7m\x1b[<65;12;7M");
  assert.deepEqual(second.reports, [
    { button: 0, x: 12, y: 7, released: false },
    { button: 0, x: 12, y: 7, released: true },
    { button: 65, x: 12, y: 7, released: false },
  ]);
  assert.equal(second.pending, "");
});

test("mouse hit testing maps visible rows to the scrolled menu items only", () => {
  const viewport = menuViewport(30, 20, 18, 5, 4);
  assert.deepEqual(viewport, { startIndex: 16, bodyRows: 9, visibleCount: 9, firstItemRow: 6 });
  assert.equal(menuItemAtRow(5, viewport, 30), null);  // header
  assert.equal(menuItemAtRow(6, viewport, 30), 16);
  assert.equal(menuItemAtRow(14, viewport, 30), 24);
  assert.equal(menuItemAtRow(15, viewport, 30), null); // footer
  assert.equal(menuItemAtRow(18, viewport, 30), null); // outside the terminal
});

test("root and nested menu hit rows use the exact header rows rendered by layout", () => {
  const rootHeader = buildMenuHeader({ items: Array(12), title: "Root" }, 30, true);
  const nestedHeader = buildMenuHeader({ items: Array(12), title: "Coding Agents" }, 30, false);
  assert.ok(rootHeader.length >= 3);
  assert.equal(nestedHeader.length, 2);

  const rootViewport = menuViewport(12, 0, 30, rootHeader.length, 4);
  const nestedViewport = menuViewport(12, 0, 30, nestedHeader.length, 4);
  assert.equal(menuItemAtRow(rootHeader.length + 1, rootViewport, 12), 0);
  assert.equal(menuItemAtRow(nestedHeader.length + 1, nestedViewport, 12), 0);
  assert.equal(menuItemAtRow(rootHeader.length, rootViewport, 12), null);
});

test("short menus map only their actual items and never expose padding as a menu action", () => {
  const viewport = menuViewport(2, 1, 24, 4, 4);
  assert.deepEqual(viewport, { startIndex: 0, bodyRows: 16, visibleCount: 2, firstItemRow: 5 });
  assert.equal(menuItemAtRow(5, viewport, 2), 0);
  assert.equal(menuItemAtRow(6, viewport, 2), 1);
  assert.equal(menuItemAtRow(7, viewport, 2), null);
});

test("mouse reports ignore releases and motion, and wheel navigation does not wrap", () => {
  assert.equal(classifySgrMouseReport({ button: 0, released: true }), null);
  assert.equal(classifySgrMouseReport({ button: 32, released: false }), null);
  assert.deepEqual(classifySgrMouseReport({ button: 65, released: false }), { type: "wheel", direction: 1 });
  const items = [{}, { disabled: true }, {}, {}];
  assert.equal(nextMouseSelection(items, 0, -1), 0);
  assert.equal(nextMouseSelection(items, 0, 1), 2);
  assert.equal(nextMouseSelection(items, 3, 1), 3);
});

test("mouse tracking is explicitly disabled when leaving the menu and restoring the terminal", () => {
  assert.equal(mouseTrackingSequence(true), "\x1b[?1000h\x1b[?1006h");
  assert.equal(mouseTrackingSequence(false), "\x1b[?1006l\x1b[?1000l");
});
