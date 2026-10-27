import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { keyboardLayout, spring, IDLE_MS } from './layout.js';

// Shares the caller's renderer; owns only its scene resources and input listeners.
export function createKeyboardScene({renderer, invalidate, post=()=>{}, offset=0, scale=1, floor=true, lighting=true}) {
const scene = new THREE.Group(); scene.position.z = offset; scene.scale.setScalar(scale);
const camera = new THREE.PerspectiveCamera(34, 1, .1, 200);
if(lighting)scene.add(new THREE.HemisphereLight(0xfff6e5, 0x6a777c, 1.8));
const keyLight = new THREE.DirectionalLight(0xfff2db, 2.1);
keyLight.position.set(-8, 18, 8); keyLight.castShadow = true;
keyLight.shadow.mapSize.set(2048, 2048); keyLight.shadow.camera.left = -18; keyLight.shadow.camera.right = 18;
keyLight.shadow.camera.top = 18; keyLight.shadow.camera.bottom = -18;
keyLight.shadow.normalBias = .035; keyLight.shadow.bias = -.00015; keyLight.shadow.radius = 3;
if(lighting)scene.add(keyLight);
const fill = new THREE.DirectionalLight(0xe0ecf6, .45); fill.position.set(10, 7, -7); if(lighting)scene.add(fill);
const groundMaterial = new THREE.MeshLambertMaterial({ color: 0xd9dfdc });
const ground = new THREE.Mesh(new THREE.PlaneGeometry(200, 200), groundMaterial);
ground.rotation.x = -Math.PI / 2; ground.position.y = -5.2; ground.receiveShadow = true; if(floor)scene.add(ground);
// A voxel desk with locally drawn pixel wood grain. No downloaded models or textures.
const grain=document.createElement('canvas');grain.width=32;grain.height=32;
const gc=grain.getContext('2d');gc.fillStyle='#a47a50';gc.fillRect(0,0,32,32);
let seed=74;const random=()=>{seed=(seed*1664525+1013904223)>>>0;return seed/4294967296;};
for(let i=0;i<85;i++){gc.fillStyle=['#986c44','#b0885b','#ad8254','#936941'][Math.floor(random()*4)];gc.fillRect(Math.floor(random()*32),Math.floor(random()*32),2+Math.floor(random()*13),1);}
gc.fillStyle='#805a37';gc.fillRect(0,0,32,1);gc.fillStyle='#b58a5c';gc.fillRect(0,1,32,1);
const woodTexture=new THREE.CanvasTexture(grain);woodTexture.magFilter=THREE.NearestFilter;woodTexture.minFilter=THREE.NearestFilter;
woodTexture.wrapS=woodTexture.wrapT=THREE.RepeatWrapping;woodTexture.repeat.set(1,3);woodTexture.colorSpace=THREE.SRGBColorSpace;
const wood=new THREE.MeshLambertMaterial({map:woodTexture,color:0xc6b49f});
const woodEdge=new THREE.MeshLambertMaterial({color:0x785337});
const desk=new THREE.Group();scene.add(desk);
for(let i=0;i<8;i++)mesh(new THREE.BoxGeometry(3.985,.65,14),wood,desk,-14+i*4,-.575,0);
mesh(new THREE.BoxGeometry(32,.42,.24),woodEdge,desk,0,-.96,6.9);
for(const x of [-15.88,15.88])mesh(new THREE.BoxGeometry(.24,.42,14),woodEdge,desk,x,-.96,0);
for(const x of [-14,14])for(const z of [-5.5,5.5])mesh(new THREE.BoxGeometry(1.15,4.3,1.15),woodEdge,desk,x,-3.05,z);
let board,caps,labelGeometry,instanceKeys=[], compact = false, dark = false, accent = new THREE.Color('#1677b9');
const keys = new Map(), visited = new Set(), held = new Set();
const baseColor = new THREE.Color(), pressedColor = new THREE.Color();
const testedTint=new THREE.Color('#62ac85'),pressedTint=new THREE.Color('#123d63');
const restingLegend=new THREE.Color('#273439'),pressedLegend=new THREE.Color('#f7fbff');
let shadowsDirty=true;
let lastTick = 0, idleTimer = 0, focused = null, disposed = false, active = true;
let lastLabel='等待按键',viewLabel='半俯视 · 全景';
let reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
const motionQuery = matchMedia('(prefers-reduced-motion: reduce)');
const onMotion = e => { reduced = e.matches; requestFrame(); };
motionQuery.addEventListener('change', onMotion);
const pose = [0, 17, 19, 0, 0, 0], velocity = [0, 0, 0, 0, 0, 0];
let target = [...pose];
const atlas = document.createElement('canvas'); atlas.width = 2048; atlas.height = 1024;
const atlasTexture = new THREE.CanvasTexture(atlas); atlasTexture.colorSpace = THREE.SRGBColorSpace;
atlasTexture.magFilter=THREE.LinearFilter;atlasTexture.minFilter=THREE.LinearMipmapLinearFilter;
atlasTexture.anisotropy=Math.min(16,renderer.capabilities.getMaxAnisotropy());
const legendMaterial = new THREE.MeshBasicMaterial({ map: atlasTexture, vertexColors:true, transparent: true, toneMapped:false, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -1 });
const raycaster = new THREE.Raycaster(), pointer = new THREE.Vector2(),instancePose=new THREE.Object3D();

function mesh(geometry, material, parent, x, y, z) {
  const m = new THREE.Mesh(geometry, material); m.position.set(x,y,z); m.castShadow = true; m.receiveShadow = true; parent.add(m); return m;
}
function build() {
  if (board) {
    scene.remove(board);
    const materials = new Set();
    board.traverse(o => { if(o.isInstancedMesh)o.dispose(); if (o.isMesh) { o.geometry.dispose(); if (o.material !== legendMaterial) materials.add(o.material); } });
    materials.forEach(m => m.dispose());
  }
  board = new THREE.Group(); scene.add(board); keys.clear();
  const defs = keyboardLayout(compact), width = compact ? 18.5 : 23;
  board.position.set(-width / 2, 0, -3.2);
  const shell = new THREE.MeshLambertMaterial({ color: 0x343e43 });
  const tray = new THREE.MeshLambertMaterial({ color: 0x1d272b });
  mesh(new THREE.BoxGeometry(width+.5,.35,6.9),shell,board,width/2,-.045,3.2);
  mesh(new THREE.BoxGeometry(width+.3,.14,6.7),tray,board,width/2,.18,3.2);
  mesh(new THREE.BoxGeometry(.11,.11,3.4),tray,board,width*.7,-.15,-1.85);
  // Caps share one draw call and legends share another, even while keys move.
  caps=new THREE.InstancedMesh(new THREE.BoxGeometry(1,.3,1),new THREE.MeshLambertMaterial(),defs.length);
  caps.castShadow=true;caps.receiveShadow=true;caps.instanceMatrix.setUsage(THREE.DynamicDrawUsage);board.add(caps);
  instanceKeys=defs.map(def=>def.id);
  const legends=[];
  defs.forEach((def,i) => {
    const group = new THREE.Group(); group.position.set(def.x+def.w/2,.33,def.z+def.h/2);
    instancePose.position.set(group.position.x,.48,group.position.z);instancePose.scale.set(def.w-.12,1,def.h-.12);instancePose.updateMatrix();
    caps.setMatrixAt(i,instancePose.matrix);caps.setColorAt(i,new THREE.Color('#e3e1d5'));
    const geo = new THREE.PlaneGeometry(Math.min(def.w-.24,1.9), Math.min(def.h-.2,.84));
    const uv = geo.attributes.uv;
    for(let j=0;j<uv.count;j++) uv.setXY(j, ((i%16)+uv.getX(j))/16, 1-(Math.floor(i/16)+1-uv.getY(j))/8);
    geo.rotateX(-Math.PI/2);geo.translate(group.position.x,.632,group.position.z);legends.push(geo);
    keys.set(def.id,{...def,group,depth:0,speed:0,color:new THREE.Color('#e3e1d5'),labelIndex:i});
  });
  labelGeometry=mergeGeometries(legends);legends.forEach(g=>g.dispose());
  labelGeometry.attributes.position.setUsage(THREE.DynamicDrawUsage);
  const colors=new THREE.Float32BufferAttribute(new Float32Array(labelGeometry.attributes.position.count*3),3);
  for(let i=0;i<colors.count;i++)colors.setXYZ(i,restingLegend.r,restingLegend.g,restingLegend.b);
  colors.setUsage(THREE.DynamicDrawUsage);labelGeometry.setAttribute('color',colors);
  board.add(new THREE.Mesh(labelGeometry,legendMaterial));
  caps.computeBoundingSphere();
  shadowsDirty=true;applyTheme(); updateStats(); goHome();
}
function drawLabels() {
  const ctx = atlas.getContext('2d'); ctx.clearRect(0,0,2048,1024);
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.fillStyle = '#ffffff';
  for (const k of keys.values()) {
    const size = k.label.length > 5 ? 26 : k.label.length > 2 ? 36 : k.label.length > 1 ? 48 : 70;
    ctx.font = `bold ${size}px Consolas,monospace`;
    ctx.fillText(k.label, (k.labelIndex%16)*128+64,Math.floor(k.labelIndex/16)*128+64,117);
    if (k.id===70 || k.id===74) { ctx.globalAlpha=.5; ctx.fillRect((k.labelIndex%16)*128+51,Math.floor(k.labelIndex/16)*128+106,26,3); ctx.globalAlpha=1; }
  }
  atlasTexture.needsUpdate = true;
}
function applyTheme() {
  groundMaterial.color.set(dark ? '#262e32' : '#d9dfdc');
  drawLabels(); requestFrame();
}
function homePose() {
  const distance = Math.max(22, (compact?20:24.6)/(2*Math.tan(THREE.MathUtils.degToRad(camera.fov/2))*camera.aspect)*1.10);
  return [distance*.24*scale,distance*.81*scale,distance*.76*scale+offset,0,-.6*scale,offset];
}
function goHome() {
  clearTimeout(idleTimer); idleTimer=0; focused=null; target=homePose();
  viewLabel='半俯视 · 全景';updateStats();requestFrame();
}
function focusKey(id,preview=false) {
  const k=keys.get(id); if(!k)return;
  focused=id;
  const x=(k.group.position.x+board.position.x)*scale, z=(k.group.position.z+board.position.z)*scale+offset;
  const distance=Math.max(8.5, 6.8/Math.max(.5,camera.aspect))*scale;
  target=[x+1.2*scale,distance*.85,z+distance*.87,x,.25*scale,z];
  viewLabel=preview ? `${k.label} · 视角预览` : `${displayName(k)} · 按键特写`;
  updateStats();requestFrame();
}
function armIdle() {
  clearTimeout(idleTimer); idleTimer=0;
  if(!held.size) idleTimer=setTimeout(goHome,IDLE_MS);
}
function displayName(k) {
  if([160,162,164,91].includes(k.id))return `左 ${k.label}`;
  if([161,163,165,92].includes(k.id))return `右 ${k.label}`;
  if(k.id===0xE01C)return '数字区 Enter';
  if(k.id>=96&&k.id<=111)return `数字区 ${k.label}`;
  return k.label;
}
function updateStats() {
  const count=[...visited].filter(id=>keys.has(id)).length;
  const active=[...held].map(id=>keys.get(id)).filter(Boolean);
  post({type:'stats',count,total:keys.size,last:lastLabel,held:active.length,view:viewLabel});
}
function receive(message) {
  if(disposed)return;
  switch(message.type) {
    case 'key': {
      const id=Number(message.key),k=keys.get(id);
      if(message.down) {
        clearTimeout(idleTimer);
        held.add(id);
        if(k){visited.add(id);lastLabel=displayName(k);focusKey(id);}
        else {lastLabel=`扩展键 0x${id.toString(16).toUpperCase()}`;}
      } else {held.delete(id);armIdle();}
      updateStats();requestFrame();break;
    }
    case 'layout': if(compact!==!!message.compact){compact=!!message.compact;build();}break;
    case 'theme': {
      dark=!!message.dark;
      if(/^#[\da-f]{6}$/i.test(message.accent||''))accent.set(message.accent);
      applyTheme();break;
    }
    case 'reset':visited.clear();lastLabel='等待按键';updateStats();goHome();break;
    case 'panorama':goHome();break;
  }
}
function requestFrame(){if(!disposed)invalidate();}
function update(now){
  if(disposed)return false;
  const dt=Math.min(.05,lastTick?(now-lastTick)/1000:1/60);lastTick=now;
  let moving=false;
  for(let i=0;i<pose.length;i++){
    [pose[i],velocity[i]]=reduced?[target[i],0]:spring(pose[i],velocity[i],target[i],dt,9);
    if(Math.abs(pose[i]-target[i])>.0001||Math.abs(velocity[i])>.0001)moving=true;
  }
  camera.position.set(...pose.slice(0,3));camera.lookAt(...pose.slice(3));
  let matricesDirty=false,colorsDirty=false,legendsDirty=false;
  for(const k of keys.values()){
    const previousDepth=k.depth;
    const down=held.has(k.id),depth=down?-.17:0;
    if(k.wasDown!==down){
      k.wasDown=down;const ink=down?pressedLegend:restingLegend;
      for(let i=0;i<4;i++)labelGeometry.attributes.color.setXYZ(k.labelIndex*4+i,ink.r,ink.g,ink.b);
      legendsDirty=true;
    }
    [k.depth,k.speed]=reduced?[depth,0]:spring(k.depth,k.speed,depth,dt,down?32:23);
    k.group.position.y=.33+k.depth;
    if(k.depth!==previousDepth){
      instancePose.position.set(k.group.position.x,.48+k.depth,k.group.position.z);instancePose.scale.set(k.w-.12,1,k.h-.12);instancePose.updateMatrix();
      caps.setMatrixAt(k.labelIndex,instancePose.matrix);matricesDirty=true;
      for(let i=0;i<4;i++)labelGeometry.attributes.position.setY(k.labelIndex*4+i,.632+k.depth);
    }
    if(Math.abs(k.depth-depth)>.0001||Math.abs(k.speed)>.0001){moving=true;shadowsDirty=true;}
    baseColor.set(k.id===27?'#c98a68':k.label.length>1?'#b6bdba':'#e3e1d5');
    if(visited.has(k.id))baseColor.lerp(testedTint,.68);
    pressedColor.copy(accent).lerp(pressedTint,.12);
    if(down)baseColor.copy(pressedColor);
    if(Math.abs(k.color.r-baseColor.r)+Math.abs(k.color.g-baseColor.g)+Math.abs(k.color.b-baseColor.b)>.001){
      k.color.lerp(baseColor,reduced?1:1-Math.exp(-dt*18));caps.setColorAt(k.labelIndex,k.color);colorsDirty=true;moving=true;
    }
  }
  if(matricesDirty){caps.instanceMatrix.needsUpdate=true;labelGeometry.attributes.position.needsUpdate=true;}
  if(colorsDirty)caps.instanceColor.needsUpdate=true;
  if(legendsDirty)labelGeometry.attributes.color.needsUpdate=true;
  if(shadowsDirty){renderer.shadowMap.needsUpdate=true;shadowsDirty=false;}
  return moving;
}
function resize(width, height){
  camera.aspect=width/Math.max(height,1);camera.updateProjectionMatrix();
  if(focused===null)target=homePose();else focusKey(focused);requestFrame();
}

function onPointer(e){
  if(!active||disposed)return;
  pointer.set(e.clientX/innerWidth*2-1,-e.clientY/innerHeight*2+1);raycaster.setFromCamera(pointer,camera);
  scene.updateMatrixWorld(true);
  const hit=raycaster.intersectObject(caps)[0];
  if(hit){focusKey(instanceKeys[hit.instanceId],true);armIdle();}
}
renderer.domElement.addEventListener('pointerdown',onPointer);
function setActive(value){
  active=value;held.clear();clearTimeout(idleTimer);idleTimer=0;lastTick=0;
  if(value)goHome();
}
function dispose(){
  if(disposed)return;disposed=true;clearTimeout(idleTimer);
  renderer.domElement.removeEventListener('pointerdown',onPointer);
  motionQuery.removeEventListener('change',onMotion);
  const geometries=new Set(),materials=new Set();
  scene.traverse(o=>{if(o.isInstancedMesh)o.dispose();if(o.isMesh){geometries.add(o.geometry);materials.add(o.material);}});
  geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());
  if(!floor){ground.geometry.dispose();groundMaterial.dispose();}
  woodTexture.dispose();atlasTexture.dispose();keyLight.shadow.dispose();
}
build();resize(innerWidth,innerHeight);target=homePose();pose.splice(0,6,...target);update(performance.now());
return {scene,camera,update,resize,receive,setActive,dispose,
  get targetPoint(){return new THREE.Vector3(...pose.slice(3));},
  setLightWeight(weight){scene.traverse(o=>{if(o.isLight){o.userData.baseIntensity??=o.intensity;o.intensity=o.userData.baseIntensity*weight;}});},
  getState:()=>({floor,lighting,compact,held:[...held],visited:[...visited],focused,pose:[...pose],target:[...target],keyCount:keys.size})};
}
