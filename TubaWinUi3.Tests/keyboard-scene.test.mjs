import test from 'node:test';
import assert from 'node:assert/strict';
import { keyboardLayout, spring, IDLE_MS } from '../TubaWinUi3.WinUI3/Assets/KeyboardTest/layout.js';

test('full and TKL layouts contain unique, non-overlapping physical keys', () => {
  for (const compact of [false,true]) {
    const keys=keyboardLayout(compact);
    assert.equal(new Set(keys.map(k=>k.id)).size,keys.length);
    for (const a of keys) for (const b of keys) {
      if(a===b)continue;
      assert.ok(a.x+a.w<=b.x+.001 || b.x+b.w<=a.x+.001 || a.z+a.h<=b.z+.001 || b.z+b.h<=a.z+.001,`${a.label} overlaps ${b.label}`);
    }
    for(const id of [160,161,162,163,164,165,91,92,0xE07E])assert.ok(keys.some(k=>k.id===id));
    assert.equal(keys.some(k=>k.id===0xE01C),!compact);
  }
  assert.equal(keyboardLayout(false).length-keyboardLayout(true).length,17);
  assert.equal(IDLE_MS,1000);
});

test('spring converges smoothly without overshoot at different refresh rates',()=>{
  for(const hz of [30,60,144]){
    let value=0,velocity=0,last=0;
    for(let i=0;i<hz*2;i++){
      [value,velocity]=spring(value,velocity,10,1/hz,9);
      assert.ok(value>=last&&value<=10);last=value;
    }
    assert.ok(Math.abs(value-10)<.001);
  }
});

test('camera retargeting preserves continuity while rapid key input reverses direction',()=>{
  let value=0,velocity=0;
  for(let i=0;i<8;i++)[value,velocity]=spring(value,velocity,10,1/60,9);
  const before=value;
  [value,velocity]=spring(value,velocity,-10,.000001,9);
  assert.ok(Math.abs(value-before)<.001,'target change must not teleport camera');
  for(let i=0;i<180;i++)[value,velocity]=spring(value,velocity,-10,1/60,9);
  assert.ok(Math.abs(value+10)<.001);
});
