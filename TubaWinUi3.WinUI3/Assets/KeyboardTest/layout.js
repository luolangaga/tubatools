// ANSI full-size keyboard, with distinct physical IDs for modifiers and numpad Enter.
export function keyboardLayout(compact = false) {
  const keys = [];
  const add = (id, label, x, z, w = 1, h = 1) => keys.push({ id, label, x, z, w, h });
  const row = (items, z) => {
    let x = 0;
    for (const [id, label, w = 1] of items) { add(id, label, x, z, w); x += w; }
  };
  add(27, 'Esc', 0, 0);
  for (let i = 0; i < 12; i++) add(112 + i, `F${i + 1}`, 2 + i + Math.floor(i / 4) * .5, 0);
  row([[192,'~  `'], ...'1234567890'.split('').map(c => [c.charCodeAt(0), c]), [189,'−  _'],[187,'+  ='],[8,'Backspace',2]], 1.4);
  row([[9,'Tab',1.5], ...'QWERTYUIOP'.split('').map(c => [c.charCodeAt(0),c]),[219,'{  ['],[221,'}  ]'],[220,'|  \\',1.5]], 2.4);
  row([[20,'Caps Lock',1.75], ...'ASDFGHJKL'.split('').map(c => [c.charCodeAt(0),c]),[186,':  ;'],[222,'"  \u0027'],[13,'Enter',2.25]], 3.4);
  row([[160,'Shift',2.25], ...'ZXCVBNM'.split('').map(c => [c.charCodeAt(0),c]),[188,'<  ,'],[190,'>  .'],[191,'?  /'],[161,'Shift',2.75]], 4.4);
  row([[162,'Ctrl',1.25],[91,'Win',1.25],[164,'Alt',1.25],[32,'Space',5],[165,'Alt',1.25],[92,'Win',1.25],[0xE07E,'Copilot',1.25],[93,'Menu',1.25],[163,'Ctrl',1.25]], 5.4);
  [[44,'PrtSc'],[145,'ScrLk'],[19,'Pause']].forEach(([id,l],i) => add(id,l,15.5+i,0));
  [[45,'Insert'],[36,'Home'],[33,'PgUp'],[46,'Delete'],[35,'End'],[34,'PgDn']].forEach(([id,l],i) => add(id,l,15.5+i%3,1.4+Math.floor(i/3)));
  add(38,'↑',16.5,4.4);
  [[37,'←'],[40,'↓'],[39,'→']].forEach(([id,l],i) => add(id,l,15.5+i,5.4));
  if (!compact) {
    [[144,'Num'],[111,'/'],[106,'×'],[109,'−']].forEach(([id,l],i) => add(id,l,19+i,1.4));
    for (let r = 0; r < 3; r++) for (let c = 0; c < 3; c++) { const n=7-r*3+c; add(96+n,`${n}`,19+c,2.4+r); }
    add(107,'+',22,2.4,1,2); add(0xE01C,'Enter',22,4.4,1,2);
    add(96,'0',19,5.4,2); add(110,'.',21,5.4);
  }
  return keys;
}

// Analytic critically damped spring. Retains velocity when the target changes mid-flight.
export function spring(value, velocity, target, dt, omega) {
  const offset = value - target, decay = Math.exp(-omega * dt);
  const j = velocity + omega * offset;
  return [target + (offset + j * dt) * decay, (velocity - omega * j * dt) * decay];
}

export const IDLE_MS = 1000;
