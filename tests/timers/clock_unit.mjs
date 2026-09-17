import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const source = await readFile(new URL('../../src/Transpiler.Backends/Runtime/services.mjs', import.meta.url), 'utf8');
const {HostClockService} = await import('data:text/javascript;base64,' + Buffer.from(source.split('class CliWeak')[0] + '\nexport {HostClockService};').toString('base64'));
class ManualClock {
    time = 0; events = [];
    now() { return this.time; }
    schedule(ms, callback) { const event = {due: this.time + ms, callback, active: true}; this.events.push(event); return event; }
    cancel(event) { event.active = false; }
    fire(event) { event.active = false; event.callback(); }
    advance(ms) { this.time += ms; for (const event of [...this.events]) if (event.active && event.due <= this.time) this.fire(event); }
}
let cases = 0;
async function test(name, action) { await action(); ++cases; console.log('PASS clock/' + name); }
function setup() { const service = new HostClockService(), clock = new ManualClock(); service.install(clock); return {service, clock}; }
await test('early-notification', () => {
    const {service: s, clock: c} = setup(); const callback = {};
    const id = s.create(10, callback); c.fire(c.events[0]);
    assert.equal(s.get(id).everQueued, false); assert.equal(s.takeReady(), null);
    assert.equal(c.events.length, 2); c.advance(10);
    assert.equal(s.takeReady(), callback); assert.equal(s.takeReady(), null); s.destroy(id);
});
await test('queued-reset-history', () => {
    const {service: s, clock: c} = setup(); const callback = {}, id = s.create(10, callback);
    c.advance(10); s.change(id, -1);
    assert.equal(s.get(id).everQueued, true); assert.equal(s.takeReady(), callback); s.destroy(id);
});
await test('stale-and-duplicate-callbacks', () => {
    const {service: s, clock: c} = setup(); const callback = {}, id = s.create(10, callback), old = c.events[0];
    s.change(id, 20); c.advance(10); c.fire(old);
    assert.equal(s.takeReady(), null); assert.equal(s.get(id).everQueued, false);
    c.advance(10); const current = c.events[1]; c.fire(current);
    assert.equal(s.takeReady(), callback); c.fire(current); assert.equal(s.takeReady(), null); s.destroy(id);
});
await test('dispose-removes-roots-and-readiness', () => {
    const {service: s, clock: c} = setup(); const id = s.create(0, {}), record = s.get(id);
    c.advance(0); s.destroy(id); s.destroy(id); c.fire(c.events[0]);
    assert.equal(record.callback, null); assert.equal(s.takeReady(), null);
    assert.equal(s.info().activeTimers, 0); assert.equal(s.info().readyTimers, 0);
});
await test('synchronous-notification-is-not-managed-execution', () => {
    const s = new HostClockService(); let calls = 0, canceled = 0;
    s.install({now: () => 0, schedule: (_, ready) => { ready(); return 1; }, cancel: () => ++canceled});
    const callback = () => ++calls, id = s.create(0, callback);
    assert.equal(calls, 0); assert.equal(canceled, 1); assert.equal(s.takeReady(), callback); s.destroy(id);
});
await test('synchronous-early-notification-is-bounded', () => {
    const s = new HostClockService(); let time = 0, schedules = 0;
    s.install({now: () => time, schedule: (_, ready) => { ++schedules; ready(); return schedules; }, cancel() {}});
    const callback = {}, id = s.create(10, callback);
    assert.equal(s.takeReady(), null); assert.equal(schedules, 2); assert.equal(s.get(id).everQueued, false);
    time = 10; assert.equal(s.takeReady(), null); assert.equal(s.takeReady(), callback); s.destroy(id);
});
await test('failed-arm-releases-record', () => {
    const s = new HostClockService(); s.install({now: () => 0, schedule() { throw new Error('arm failed'); }, cancel() {}});
    assert.throws(() => s.create(1, {}), /arm failed/); assert.equal(s.info().activeTimers, 0);
});
await test('range-and-handle-budget', () => {
    const {service: s} = setup();
    for (const n of [-2, 2147483648, 0.5, NaN]) assert.throws(() => s.create(n, {}));
    s.nextId = 2147483648; assert.throws(() => s.create(1, {}), /budget/); assert.equal(s.info().activeTimers, 0);
});
await test('broadcast-and-generation-wakeup', async () => {
    const {service: s} = setup(); const id = s.create(10, {}), version = s.version;
    const a = s.wait(version), b = s.wait(version); assert.equal(s.info().hostWaiters, 2);
    s.signal(); await Promise.all([a, b]); assert.equal(s.info().hostWaiters, 0);
    await s.wait(version); assert.equal(s.info().hostWaiters, 0); s.destroy(id);
});
await test('abort-detaches-waiter', async () => {
    const {service: s} = setup(); const id = s.create(10, {}), controller = new AbortController();
    const result = s.wait(s.version, controller.signal); controller.abort(new Error('stop'));
    await assert.rejects(result, /stop/); assert.equal(s.info().hostWaiters, 0); s.destroy(id);
});
await test('active-clock-replacement-rejected', () => {
    const {service: s} = setup(); const id = s.create(10, {});
    assert.throws(() => s.install(null), /live timers/); s.destroy(id); s.install(null);
});
await test('timer-fifo-and-disposal-stress', () => {
    const {service: s, clock: c} = setup(), expected = [];
    for (let i = 0; i < 256; ++i) { const item = {i}, id = s.create(10, item); if (i % 2) expected.push([id, item]); else s.destroy(id); }
    c.advance(10);
    for (const [id, item] of expected) { assert.equal(s.takeReady(), item); s.destroy(id); }
    for (const event of c.events) c.fire(event);
    assert.equal(s.takeReady(), null); assert.equal(s.info().activeTimers, 0); assert.equal(s.info().readyTimers, 0);
});
await test('native-timer-waits-without-polling', async () => {
    const s = new HostClockService(), callback = {}, id = s.create(20, callback);
    let ready = null;
    for (let attempt = 0; attempt < 16 && ready === null; ++attempt) { await s.wait(s.version); ready = s.takeReady(); }
    assert.equal(ready, callback); s.destroy(id);
    assert.equal(s.info().hostWaiters, 0); assert.equal(s.info().activeTimers, 0);
});
console.log(JSON.stringify({cases, passed: cases}));
