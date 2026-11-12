// Skia needs native code (or CanvasKit), so tests stub the drawing primitives; charts are checked on the simulator
// (/dev/gallery?only=charts). Path builders accept every call and build an empty path.
const Stub = () => null;

const builder = () => {
  const b: Record<string, unknown> = {};
  for (const method of ['moveTo', 'lineTo', 'addRect', 'close']) b[method] = () => b;
  b.build = () => ({});
  return b;
};

module.exports = {
  Canvas: Stub,
  Path: Stub,
  Line: Stub,
  Group: Stub,
  Rect: Stub,
  DashPathEffect: Stub,
  Skia: { PathBuilder: { Make: builder }, XYWHRect: () => ({}) },
  vec: (x: number, y: number) => ({ x, y }),
};
