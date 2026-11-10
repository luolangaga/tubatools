import * as THREE from 'three';
import { box, batchStatic, cylinder, impeller, roundedRect, roundHole, panel, plate, turned, cable } from './geometry.js';
import { desktopInterior } from './desktop-interior.js';
import { monitorBack } from './monitor-back.js';
import { desktopMouse } from './desktop-mouse.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { HardwareFocusController } from './focus.js';
import { HardwareStressEffect } from './stress.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { createKeyboardScene } from '../KeyboardTest/scene-controller.js';
import { InspectionCompletionEffect } from './completion.js';

// Local procedural models plus licensed component GLBs; see models/README.md. No remote requests.
const renderer=new THREE.WebGLRenderer({antialias:true,alpha:true,preserveDrawingBuffer:true});
renderer.setPixelRatio(Math.min(devicePixelRatio,2));renderer.setClearColor(0,0);
renderer.outputColorSpace=THREE.SRGBColorSpace;document.body.appendChild(renderer.domElement);
renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1;
const scene=new THREE.Scene();
const environment=new RoomEnvironment();
const environmentGenerator=new THREE.PMREMGenerator(renderer);
const environmentMap=environmentGenerator.fromScene(environment,.04);environmentGenerator.dispose();
scene.environment=environmentMap.texture;environment.dispose();
scene.add(new THREE.HemisphereLight(0xe8f2ff,0x253343,1.6));
const studioLight=new THREE.DirectionalLight(0xffffff,3.2);studioLight.position.set(-3,6,5);scene.add(studioLight);
const fillLight=new THREE.DirectionalLight(0x73c8ed,1.4);fillLight.position.set(4,2,-2);scene.add(fillLight);
const camera=new THREE.PerspectiveCamera(34,1,0.1,100);
const controls=new OrbitControls(camera,renderer.domElement);
renderer.domElement.tabIndex=0;
renderer.domElement.setAttribute('aria-label','3D 电脑模型，拖动或方向键旋转');
renderer.domElement.addEventListener('keydown',e=>{if(e.key==='Escape'&&componentFocus.active){e.preventDefault();window.chrome?.webview?.postMessage({type:'dismissFocus',requestId:componentFocus.requestId});return;}const directions={ArrowLeft:[-12,0],ArrowRight:[12,0],ArrowUp:[0,-12],ArrowDown:[0,12]};if(directions[e.key]){e.preventDefault();receive({type:'drag',dx:directions[e.key][0],dy:directions[e.key][1]});}});
controls.enablePan=false;controls.enableDamping=true;controls.dampingFactor=0.12;
controls.enableZoom=false;controls.minPolarAngle=.15;controls.maxPolarAngle=Math.PI*.75;
controls.addEventListener('change',requestFrame);
controls.addEventListener('start',()=>{inspectionMotion=null;autoRotate=false;window.chrome?.webview?.postMessage({type:'autoStopped'});});
let frame=0,paused=false,openingStart=0,opening=false,lid,autoRotate=false,lastPost=0,config={},reducedMotion=false,lastTick=0,lastAnchors="";
let inspectionMotion=null,inspectionOverview=null,inspectionLabel=null;
let completionActive=false;
let keyboardScene=null,keyboardActive=false,keyboardBlend=0,keyboardMotion=null,keyboardPreparation=null,keyboardRequest=0,keyboardWarming=false;
let bufferLockUntil=0,bufferWidth=0,bufferHeight=0,resizeTimer=0;
const displayCamera=camera.clone();
const computerMaterials=new Map();
let computerOpacity=1;
let computerFadeMotion=null;
const keyboardTransitionMs=1100;
let lastStressFrame=0;
const rotors=[];
const internalRotors=[];
const fanTrailMaterials=[];
let fanSpeed=4;
let wireMaterial=new THREE.LineBasicMaterial({color:0x94b9d5,transparent:true,opacity:.82});
const accentWire=new THREE.LineBasicMaterial({color:0x67d9ff,transparent:true,opacity:.95});
const hud=document.createElementNS('http://www.w3.org/2000/svg','svg');hud.setAttribute('viewBox','0 0 1000 1000');hud.setAttribute('preserveAspectRatio','none');hud.style.cssText='position:absolute;inset:0;width:100%;height:100%;pointer-events:none';document.body.append(hud);
function requestFrame(){if(!frame&&!paused)frame=requestAnimationFrame(tick);}
function projectedAnchors(){const anchors={};for(const [name,obj] of Object.entries(markers)){const p=obj.getWorldPosition(new THREE.Vector3()).project(camera);anchors[name]=[(p.x+1)/2,(1-p.y)/2];}return anchors;}
function updateHud(anchors){
 if(config.embedded||!config.connectors){hud.innerHTML='';return;}
 hud.innerHTML=Object.entries(anchors).filter(([name])=>config.labels?.includes(name)).map(([name,[x,y]])=>{
  const start=config.starts?.[name];
  if(!start)return '';
  return `<line x1="${start[0]*1000}" y1="${start[1]*1000}" x2="${x*1000}" y2="${y*1000}" stroke="#69c6e4" stroke-width="2.3" opacity=".7"/>`;
 }).join('');
}
function tick(now){
 frame=0;if(paused||!model||document.hidden||keyboardWarming)return;
 if(config.inspection&&(stressEffect.active||rotors.length)&&!completionActive&&!inspectionMotion&&!opening&&!keyboardMotion&&!keyboardActive&&now-lastStressFrame<(reducedMotion?250:1000/24)){requestFrame();return;}
 lastStressFrame=now;
 const delta=Math.min(.05,lastTick?(now-lastTick)/1000:1/60);lastTick=now;
 const fanTarget=config.inspection?(stressEffect.active?28:4):8;
 fanSpeed+=(fanTarget-fanSpeed)*(1-Math.exp(-delta*2.4));
 if(!completionActive&&(!config.inspection||!reducedMotion)){
  for(const rotor of rotors)rotor.rotation.z-=delta*fanSpeed;
  if(!reducedMotion)for(const {rotor,rate} of internalRotors)rotor.rotation.z-=delta*fanSpeed*rate;
 }
 for(const material of fanTrailMaterials)material.opacity=!config.inspection||reducedMotion?0:Math.max(0,(fanSpeed-4)/24)*.16;
 if(opening){const progress=reducedMotion?1:Math.min(1,(now-openingStart)/1500);const ease=1-Math.pow(1-progress,3);
  if(lid)lid.rotation.x=Math.PI/2-(Math.PI/2+.19)*ease;
  model.rotation.y=.45*(1-ease);screenMaterial.opacity=config.laptop===false?Math.max(0,(progress-.35)/.65):Math.max(0,(progress-.45)/.55);
  accentWire.opacity=config.laptop===false?ease:.95;
  if(progress===1){opening=false;setTimeout(()=>{postRender(false);window.chrome?.webview?.postMessage({type:'booted'});},0);}
 }
 if(inspectionMotion){
  const m=inspectionMotion,t=reducedMotion?1:Math.min(1,(now-m.start)/850),s=t*t*t*(10+t*(-15+6*t));
  camera.position.lerpVectors(m.from,m.to,s);controls.target.lerpVectors(m.fromTarget,m.target,s);camera.lookAt(controls.target);
  if(t===1)inspectionMotion=null;
 }
 const focusAnimating=componentFocus.update(now,delta);
 controls.autoRotate=autoRotate&&!opening&&!componentFocus.active;controls.autoRotateSpeed=.5;if(!componentFocus.active)controls.update();
 let keyboardMoving=false;
 if(keyboardScene&&(keyboardActive||keyboardBlend>0))keyboardMoving=keyboardScene.update(now);
 if(keyboardMotion){
  const m=keyboardMotion,t=reducedMotion?1:Math.min(1,(now-m.start)/keyboardTransitionMs),s=t*t*t*(10+t*(-15+6*t));
  keyboardBlend=THREE.MathUtils.lerp(m.from,m.to,s);
  if(t===1){keyboardMotion=null;if(!keyboardActive)keyboardScene.scene.visible=false;}
 }
 // Camera arrival does not wait for the gentler computer fade to finish.
 if(computerFadeMotion){
  const m=computerFadeMotion,t=reducedMotion?1:Math.max(0,Math.min(1,(now-m.start)/m.duration)),s=t*t*t*(10+t*(-15+6*t));
  setComputerOpacity(THREE.MathUtils.lerp(m.from,m.to,s));
  if(t===1)computerFadeMotion=null;
 }
 scene.children.filter(o=>o.isLight).forEach(o=>{
  if(keyboardBlend>0){o.userData.inspectionIntensity??=o.intensity;o.intensity=o.userData.inspectionIntensity*(1-keyboardBlend);}
  else if(o.userData.inspectionIntensity!==undefined){o.intensity=o.userData.inspectionIntensity;delete o.userData.inspectionIntensity;}
 });
 let renderCamera=camera;
 if(keyboardScene&&keyboardBlend>0){
  keyboardScene.setLightWeight(keyboardBlend);
  displayCamera.copy(camera);
  displayCamera.position.lerpVectors(camera.position,keyboardScene.camera.position,keyboardBlend);
  displayCamera.lookAt(controls.target.clone().lerp(keyboardScene.targetPoint,keyboardBlend));
  renderCamera=displayCamera;
 }
 scene.updateMatrixWorld(true);camera.updateMatrixWorld(true);
 const anchors=keyboardActive?{}:projectedAnchors();stressEffect.update(now,anchors,reducedMotion);renderer.render(scene,renderCamera);updateHud(anchors);
 const signature=JSON.stringify(anchors);
 const celebrating=completionEffect.update(now);
 if(!config.inspection&&now-lastPost>32&&signature!==lastAnchors){lastPost=now;lastAnchors=signature;window.chrome?.webview?.postMessage({type:'anchors',anchors});}
 if(opening||autoRotate||(!completionActive&&!keyboardActive&&rotors.length&&(!config.inspection||!reducedMotion))||focusAnimating||inspectionMotion||stressEffect.active||keyboardMotion||computerFadeMotion||keyboardMoving||celebrating)requestFrame();
}
function setComputerOpacity(value){
 if(!model||value===computerOpacity)return;
 computerOpacity=value;model.visible=value>0;
 if(value<1&&!computerMaterials.size)model.traverse(object=>{
  for(const material of object.material?(Array.isArray(object.material)?object.material:[object.material]):[])
   if(!computerMaterials.has(material))computerMaterials.set(material,{opacity:material.opacity,transparent:material.transparent});
 });
 for(const [material,original] of computerMaterials){
  const transparent=value<1||original.transparent;
  if(material.transparent!==transparent){material.transparent=transparent;material.needsUpdate=true;}
  material.opacity=original.opacity*value;
 }
 if(value===1)computerMaterials.clear();
}
function postRender(withImage=false){if(!model)return;renderer.render(scene,camera);const result={type:'rendered',anchors:projectedAnchors()};if(withImage)result.image=renderer.domElement.toDataURL('image/png');window.chrome?.webview?.postMessage(result);window.lastResult=result;return result;}
function resize(){
 const w=innerWidth||720,h=innerHeight||690,locked=performance.now()<bufferLockUntil;
 const bw=locked?Math.max(bufferWidth,w):w,bh=locked?Math.max(bufferHeight,h):h;
 if(bw!==bufferWidth||bh!==bufferHeight){renderer.setSize(bw,bh,false);bufferWidth=bw;bufferHeight=bh;}
 // Keep one drawing buffer while the native viewport expands; only its display size changes.
 renderer.domElement.style.width=w+'px';renderer.domElement.style.height=h+'px';
 camera.aspect=w/h;camera.fov=config.inspection?34:THREE.MathUtils.radToDeg(2*Math.atan(Math.tan(34*Math.PI/360)*h/360));camera.updateProjectionMatrix();keyboardScene?.resize(w,h);
 if(config.inspection&&model&&!componentFocus.active&&!inspectionLabel){if(completionActive)focusCompletionOverview();else fitInspectionOverview();}requestFrame();
}
addEventListener('resize',resize);resize();
const aluminum=new THREE.MeshStandardMaterial({color:0x7c8995,metalness:0.65,roughness:0.38,envMapIntensity:.65});
const edge=new THREE.MeshStandardMaterial({color:0x202733,metalness:0.08,roughness:0.7});
const keys=new THREE.MeshStandardMaterial({color:0x10151d,metalness:0.05,roughness:0.8});
const dark=new THREE.MeshStandardMaterial({color:0x080e17,metalness:0.06,roughness:0.7});
const accent=new THREE.MeshStandardMaterial({color:0x68d6ff,emissive:0x009dce,emissiveIntensity:1.2,metalness:0.25,roughness:0.25});
const trim=new THREE.MeshStandardMaterial({color:0x425161,metalness:.45,roughness:.4});
const trackpad=new THREE.MeshStandardMaterial({color:0x718191,metalness:.42,roughness:.58});
const lens=new THREE.MeshStandardMaterial({color:0x233d60,metalness:.5,roughness:.13});
const portBlue=new THREE.MeshStandardMaterial({color:0x286083,roughness:.65});
const contactGold=new THREE.MeshStandardMaterial({color:0xa68a51,metalness:.52,roughness:.43});
const fanBlade=new THREE.MeshStandardMaterial({color:0x7790a3,metalness:.35,roughness:.48});
let model,markers={},screenMaterial;
const componentFocus=new HardwareFocusController({scene,camera,controls,canvas:renderer.domElement,
 getModel:()=>model,getMarker:label=>markers[label]||markers['主板'],getScreenTexture:()=>screenMaterial?.map,reduced:()=>reducedMotion,requestFrame,prepareObject:prepareComponentObject,
 post:message=>window.chrome?.webview?.postMessage(message),restoreAuto:value=>{autoRotate=value;}});
const stressEffect=new HardwareStressEffect(()=>screenMaterial,studioLight,fillLight);
const completionEffect=new InspectionCompletionEffect();
async function prepareComponentObject(object){
 // Warm textures, shaders and vertex buffers before any focus motion is timed.
 const holder=new THREE.Group(),culling=[],textures=new Set();
 holder.scale.setScalar(.00001);holder.position.copy(model.position);holder.add(object);scene.add(holder);
 object.traverse(node=>{if(node.isMesh){
  culling.push([node,node.frustumCulled]);node.frustumCulled=false;
  for(const material of Array.isArray(node.material)?node.material:[node.material])
   for(const value of Object.values(material))if(value?.isTexture)textures.add(value);
 }});
 try{
  for(const texture of textures)renderer.initTexture(texture);
  await renderer.compileAsync(object,camera,scene);
  renderer.render(scene,camera);
 }finally{
  scene.remove(holder);holder.remove(object);
  for(const [node,culled] of culling)node.frustumCulled=culled;
  object.updateMatrixWorld(true);
 }
}
function plane(parent,w,h,x,y,z,mat){const m=new THREE.Mesh(new THREE.PlaneGeometry(w,h),mat);m.position.set(x,y,z);parent.add(m);return m;}
function marker(name,parent,x,y,z){const o=new THREE.Object3D();o.position.set(x,y,z);parent.add(o);markers[name]=o;}
function screenSurface(parent,w,h,r,x,y,z){
 const geometry=new THREE.ShapeGeometry(roundedRect(w,h,r),10),p=geometry.attributes.position,uv=geometry.attributes.uv;
 for(let i=0;i<uv.count;i++)uv.setXY(i,p.getX(i)/w+.5,p.getY(i)/h+.5);
 const mesh=new THREE.Mesh(geometry,screenMaterial);mesh.position.set(x,y,z);parent.add(mesh);
}
function lensStack(parent,x,y,z,r){
 const ring=turned(parent,[[r*.60,.001],[r*.64,.005],[r*.89,.005],[r,.002],[r,0],[r*.60,0],[r*.60,.001]],x,y,z,trim,32);
 ring.rotation.x=Math.PI/2;
 const optic=new THREE.Mesh(new THREE.SphereGeometry(r*.62,24,12),lens);
 optic.scale.z=.31;optic.position.set(x,y,z+.001);parent.add(optic);
}
function display(parent,w,h,x,y,z,laptop=false){
 const wActive=w-(laptop ? .108 : .062),hActive=wActive/(laptop?1.6:16/9);
 const panelY=y+(laptop ? -.008 : .009),front=z+(laptop ? .020 : .030);
 const shell=roundedRect(w,h,laptop ? .077 : .048);
 panel(parent,shell,laptop ? .036 : .049,x,y,z,aluminum,laptop ? .004 : .0045,12);
 if(!laptop)monitorBack(parent,w,x,y,z,{shell:edge,dark,metal:aluminum,trim,portBlue});
 panel(parent,roundedRect(w-(laptop ? .02 : .03),h-.019,laptop ? .060 : .035),.008,x,y,front,edge,.002,10);
 const glass=roundedRect(w-(laptop ? .048 : .033),h-(laptop ? .036 : .024),laptop ? .049 : .026);
 panel(parent,glass,.004,x,y,front+.006,dark,.001,12);
 screenSurface(parent,wActive,hActive,laptop ? .035 : .012,x,panelY,front+.012);
 if(laptop){
  const eyeY=y+h/2-.027;
  panel(parent,roundedRect(.196,.035,.018),.002,x,eyeY,front+.014,keys,.001,9);
  lensStack(parent,x,eyeY,front+.017,.013);
  lensStack(parent,x+.047,eyeY,front+.017,.0085);
  panel(parent,roundedRect(.038,.007,.0035),.002,x-.028,y+h/2-.009,front+.016,trim,.0005,6);
  for(let i=0;i<3;i++)box(parent,.002,.004,.0009,x-.035+i*.006,y+h/2-.009,front+.0175,dark);
  for(const dx of [-.119,.107])cylinder(parent,.0031,.001,x+dx,eyeY,front+.0132,trim,14).rotation.x=Math.PI/2;
  return eyeY;
 }
 // Separate clip-on camera, with a glass lens and moulded hinge.
 const eyeY=y+h/2+.054;
 panel(parent,roundedRect(.196,.066,.027),.079,x,eyeY,z+.028,edge,.0045,12);
 panel(parent,roundedRect(.172,.049,.022),.006,x,eyeY,z+.071,dark,.002,10);
 lensStack(parent,x,eyeY,z+.077,.021);
 for(const dx of [-.063,.063])for(let i=0;i<3;i++)cylinder(parent,.002,.001,x+dx,eyeY-.006+i*.005,z+.076,trim,9).rotation.x=Math.PI/2;
 box(parent,.089,.025,.092,x,y+h/2+.001,z-.011,keys,.007);
 box(parent,.073,.062,.018,x,y+h/2-.024,z-.059,edge,.006).rotation.x=.24;
 cylinder(parent,.014,.094,x,y+h/2+.014,z-.030,trim,24).rotation.z=Math.PI/2;
 box(parent,.019,.004,.003,x+w*.44,y-h/2+.012,front+.012,accent,.001);
 return eyeY;
}
const numberRow=[...'1234567890'].map(k=>[k,1]);
const letterRow=(text)=>[...text].map(k=>[k,1]);
const keyRows=[
 [['esc',1],...Array.from({length:12},(_,i)=>['F'+(i+1),1]),['del',1.5]],
 [['~',1],...numberRow,['-',1],['=',1],['⌫',1.5]],
 [['tab',1.35],...letterRow('QWERTYUIOP[]'),['\\',1.15]],
 [['caps',1.55],...letterRow("ASDFGHJKL;'"),['enter',1.95]],
 [['shift',1.95],...letterRow('ZXCVBNM,./'),['shift',2.55]],
 [['ctrl',1.3],['fn',1],['⊞',1],['alt',1.1],['',5.2],['alt',1.1],['←',.9],['↑\n↓',1],['→',1]]
];
function keyboardCaps(parent,cx,cz,w,d,top,capHeight,lowProfile=false){
 const atlas=document.createElement('canvas');atlas.width=1024;atlas.height=512;
 const ctx=atlas.getContext('2d');ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillStyle='#dce8f2';
 const map=new THREE.CanvasTexture(atlas);map.colorSpace=THREE.SRGBColorSpace;
 map.anisotropy=Math.min(4,renderer.capabilities.getMaxAnisotropy());
 const ink=new THREE.MeshBasicMaterial({map,transparent:true,toneMapped:false,depthWrite:false});
 ink.userData.ownsMap=true;
 const mainW=lowProfile?w:w*.846,mainX=cx-(w-mainW)/2;
 const rowUnits=lowProfile?[.76,1,1,1,1,1]:[.88,1,1,1,1,1],rowUnit=d/rowUnits.reduce((a,b)=>a+b,0);
 let rowStart=cz-d/2;
 const rows=lowProfile?keyRows:[...keyRows.slice(0,-1),[['ctrl',1.25],['⊞',1.25],['alt',1.25],['',6.25],['alt',1.25],['fn',1.25],['ctrl',1.25]]];
 const legend=(symbol,secondary,cell,x,z,w,d)=>{
  const tx=cell%16,ty=Math.floor(cell/16);
  ctx.font=(symbol.length>2?'19':'25')+'px Segoe UI, sans-serif';
  ctx.fillText(symbol,tx*64+32,ty*36+(secondary?25:18),57);
  if(secondary){ctx.font='11px Segoe UI, sans-serif';ctx.fillText(secondary,tx*64+32,ty*36+8,51);}
  const glyph=plane(parent,w,d,x,top+capHeight+(lowProfile ? .0005 : .003),z,ink);
  glyph.rotation.x=-Math.PI/2;
  const uv=glyph.geometry.attributes.uv;
  for(let j=0;j<uv.count;j++)uv.setXY(j,(tx*64+uv.getX(j)*64)/1024,1-(ty*36+(1-uv.getY(j))*36)/512);
 };
 const switchHousing=new THREE.MeshStandardMaterial({color:0x485766,roughness:.64});
 const sculpt=(width,depth,x,z,row)=>{
  const tall=lowProfile?1:1+(.028*(2-row));
  const cap=plate(parent,width,depth,lowProfile ? .017 : .016,capHeight-.005,x,top+capHeight/2-.001,z,keys,.0025,4);
  const p=cap.geometry.attributes.position;
  // Sculpted desktop rows have a wider skirt around the stem and a narrower top.
  for(let i=0;i<p.count;i++){
   const upper=THREE.MathUtils.clamp(p.getZ(i)/capHeight+.5,0,1);
   const dx=(lowProfile ? .004 : .015)*upper,dz=(lowProfile ? .005 : .014)*upper;
   const v=p.getY(i),tilt=lowProfile?0:(row-2)*.016;
   p.setXYZ(i,p.getX(i)*(1-dx/width),v*(1-dz/depth),p.getZ(i)*tall+v*tilt);
  }
  cap.geometry.computeVertexNormals();
  if(!lowProfile){
   box(parent,Math.min(.058,width*.64),.016,.052,x,top-.007,z,switchHousing,.002);
   if(width>.18)for(const dx of [-.29,.29])box(parent,.033,.011,.046,x+dx*width,top-.008,z,trim,.002);
  }
 };
 for(const [row,entries] of rows.entries()){
  const rowDepth=rowUnits[row]*rowUnit;
  const unit=mainW/entries.reduce((sum,[,size])=>sum+size,0);
  let cursor=mainX-mainW/2;
  for(const [col,[label,size]] of entries.entries()){
   const kw=unit*size,x=cursor+kw/2,z=rowStart+rowDepth/2;
   const half=label.includes('\n'),symbols=label.split('\n');
   cursor+=kw;
   for(const [i,symbol] of symbols.entries()){
    const capD=(rowDepth-.019)/symbols.length;
    const keyZ=z+(half?(i-.5)*capD:0);
    const width=kw-.022,depth=capD-(half ? .009 : .002);
    sculpt(width,depth,x,keyZ,row);
    if(!symbol)continue;
    const cell=row*32+col*2+i;
    const upper=['~','!','@','#','$','%','^','&','*','(',')','_','+'][col];
    const secondary=row===1&&col>0&&col<13?upper:null;
    legend(!lowProfile&&row===0&&symbol==='del'?'ins':symbol,secondary,cell,x,keyZ,Math.min(kw-.032,unit*.84),capD*.79);
   }
  }
  rowStart+=rowDepth;
 }
 if(!lowProfile){
  const navX=cx+w*.425,navZ=cz+d*.293;
  const nav=[['del',-.055,-.294],['home',.055,-.294],['pgup',-.055,-.200],['pgdn',.055,-.200],
   ['↑',0,-.014],['←',-.093,.079],['↓',0,.079],['→',.093,.079]];
  for(const [i,[symbol,dx,dz]] of nav.entries()){
   sculpt(.083,.079,navX+dx,navZ+dz,i<4?1:4);
   legend(symbol,null,192+i,navX+dx,navZ+dz,.072,.060);
  }
 }
}

function desktopKeyboard(root){
 const keyboard=new THREE.Group();keyboard.position.set(-1.03,0,1.04);root.add(keyboard);
 const body=plate(keyboard,2.224,.700,.073,.037,0,.034,0,aluminum,.006,9);
 const p=body.geometry.attributes.position;
 for(let i=0;i<p.count;i++){
  const ramp=THREE.MathUtils.clamp((p.getY(i)+.350)/.700,0,1);
  const upper=THREE.MathUtils.clamp(p.getZ(i)/.037+.5,0,1);
  p.setZ(i,p.getZ(i)+ramp*upper*.063);
 }
 body.geometry.computeVertexNormals();
 const top=new THREE.Group();top.position.y=.086;top.rotation.x=.091;keyboard.add(top);
 const bezel=roundedRect(2.203,.686,.063);
 bezel.holes.push(roundedRect(2.115,.612,.040,0,0,new THREE.Path()));
 panel(top,bezel,.014,0,0,0,trim,.003,7).rotation.x=-Math.PI/2;
 plate(top,2.112,.611,.040,.007,0,-.003,0,edge,.001,8);
 keyboardCaps(top,0,0,2.100,.603,.015,.041);
 for(const x of [-1.052,1.052])for(const z of [-.292,.292]){
  cylinder(top,.0095,.003,x,.0075,z,aluminum,16);
  box(top,.013,.0012,.002,x,.0094,z,dark).rotation.y=.72;
 }
 turned(top,[[0,.004],[.032,.004],[.038,.009],[.038,.028],[.031,.032],[0,.032]],.947,.008,-.244,trim,36);
 for(let i=0;i<24;i++){
  const a=i*Math.PI/12;
  box(top,.003,.015,.003,.947+Math.cos(a)*.038,.026,-.244+Math.sin(a)*.038,edge).rotation.y=-a;
 }
 const port=roundedRect(.078,.030,.012);port.holes.push(roundedRect(.055,.019,.008,0,0,new THREE.Path()));
 panel(keyboard,port,.006,-.794,.089,-.350,trim,0,6).rotation.y=Math.PI;
 box(keyboard,.048,.011,.006,-.794,.089,-.347,dark,.002);
 box(keyboard,.035,.004,.006,-.794,.089,-.354,edge,.001);
 for(const x of [-.887,.887])for(const z of [-.244,.232])
  plate(keyboard,.212,.085,.039,.012,x,.012,z,keys,.002,7);
 batchStatic(top);batchStatic(keyboard);return keyboard;
}

function sideSocket(parent,side,z,w,h,kind){
 const x=side*1.650,y=.090,outer=roundedRect(w+.024,h+.015,kind==='c' ? .021 : .009);
 const aperture=roundedRect(w,h,kind==='c'?h/2:.006,0,0,new THREE.Path());
 outer.holes.push(aperture);
 const rim=panel(parent,outer,.009,x,y,z,trim,.0014,10);rim.rotation.y=side*Math.PI/2;
 const seat=panel(parent,roundedRect(w-.003,h-.002,kind==='c' ? .015 : .005),.004,x-side*.010,y,z,dark,0,10);
 seat.rotation.y=side*Math.PI/2;
 if(kind==='card'){
  box(parent,.008,.005,w*.73,x-side*.005,y-.006,z,edge,.001);
  return;
 }
 const tabH=kind==='c' ? .007 : .011,tabY=y+(kind==='c'?0:-.007);
 box(parent,.013,tabH,w*.75,x-side*.001,tabY,z,kind==='a'?portBlue:keys,.0015);
 const count=kind==='c'?12:kind==='video'?9:4,pitch=w*.66/count;
 for(let i=0;i<count;i++){
  const contactZ=z+(i-(count-1)/2)*pitch;
  box(parent,.010,.0016,pitch*.48,x-side*.003,tabY+tabH/2+.001,contactZ,contactGold);
  if(kind==='c'||kind==='video')box(parent,.010,.0016,pitch*.48,x-side*.003,tabY-tabH/2-.001,contactZ,contactGold);
 }
}
function laptop(){
 const root=new THREE.Group();
 // CNC deck and flush inlays; the lower cover rises toward the front edge.
 const deck=roundedRect(3.306,2.182,.112);
 deck.holes.push(roundedRect(2.93,1.013,.042,0,.332,new THREE.Path()));
 deck.holes.push(roundedRect(1.491,.647,.062,0,-.548,new THREE.Path()));
 for(const side of [-1,1])for(let col=0;col<3;col++)for(let row=0;row<34;row++)
  roundHole(deck,.0053,side*(1.514+col*.022),.826-row*.027);
 const top=panel(root,deck,.014,0,.127,0,aluminum,.0028,7);top.rotation.x=-Math.PI/2;
 const rail=plate(root,3.264,2.144,.103,.074,0,.079,0,trim,.006,11);
 const bottom=roundedRect(3.258,2.144,.107);
 for(const side of [-1,1])for(let i=0;i<20;i++)for(const z of [.537,.742]){
  const x=side*(.333+i*.052);
  bottom.holes.push(roundedRect(.025,.125,.012,x,z,new THREE.Path()));
 }
 const underside=panel(root,bottom,.023,0,.030,0,aluminum,0,5);underside.rotation.x=-Math.PI/2;
 for(const [mesh,factor] of [[rail,.5],[underside,1]]){
  const p=mesh.geometry.attributes.position;
  for(let i=0;i<p.count;i++){
   const front=THREE.MathUtils.clamp((1.08-p.getY(i))/2.16,0,1);
   const taper=mesh===rail?Math.max(0,.5-p.getZ(i)/.074):1;
   p.setZ(i,p.getZ(i)+.030*front*factor*taper);
  }
  mesh.geometry.computeVertexNormals();
 }
 plate(root,2.919,1.004,.039,.006,0,.119,-.332,edge,.001,9);
 keyboardCaps(root,0,-.333,2.865,.946,.123,.020,true);
 plate(root,1.488,.645,.060,.004,0,.123,.548,dark,.001,10);
 plate(root,1.469,.624,.056,.010,0,.128,.548,trackpad,.0018,12);
 for(const side of [-1,1]){
  plate(root,.087,.934,.034,.002,side*1.536,.123,-.38,dark,.0005,7);
  // Sound exits the perforated deck; the cloth and grille cavity sit below it.
  for(let row=0;row<34;row++)box(root,.066,.0014,.003,side*1.536,.125,-.826+row*.027,keys);
  for(const [z,y] of [[-.906,.008],[.78,.027]]){
   plate(root,.46,.066,.033,.018,side*1.025,y,z,keys,.003,8);
   plate(root,.43,.048,.023,.003,side*1.025,y-.010,z,edge,.001,6);
   for(let i=0;i<12;i++)box(root,.003,.0015,.030,side*1.025+(i-5.5)*.031,y-.012,z,trim).rotation.y=.25;
  }
  for(const z of [-.92,-.02,.87]){
   const y=.017+.030*(1.08+z)/2.16;
   cylinder(root,.026,.003,side*1.46,y,z,trim,20);
   cylinder(root,.018,.003,side*1.46,y-.0025,z,aluminum,18);
   for(const angle of [.72,.72+Math.PI/2])box(root,.019,.0012,.0031,side*1.46,y-.0042,z,dark).rotation.y=angle;
  }
  plate(root,1.079,.356,.040,.002,side*.823,.045,-.638,dark,0,5);
  for(let i=0;i<20;i++){
   const x=side*(.333+i*.052);
   box(root,.020,.003,.292,x,.043,-.641,keys);
   for(const z of [-.711,-.563])box(root,.020,.003,.003,x,.041,z,trim);
  }
  for(const x of [.55,1.35]){
   const screw=cylinder(root,.016,.003,side*x,.013,-.965,trim,16);
   screw.rotation.z=.006;
   for(const a of [0,Math.PI/3,Math.PI*2/3])box(root,.015,.001,.003,side*x,.011,-.965,dark).rotation.y=a;
  }
  // A fine break marks the removable lower cover, above the tapered corner.
  box(root,.002,.003,1.778,side*1.634,.061,-.004,edge);
 }
 for(const [side,z,w,h,kind] of [[-1,-.67,.094,.038,'c'],[-1,-.475,.094,.038,'c'],[-1,-.222,.172,.052,'video'],[1,-.591,.149,.047,'a'],[1,-.378,.094,.038,'c'],[1,.081,.231,.024,'card']])
  sideSocket(root,side,z,w,h,kind);
 const jack=turned(root,[[.016,-.007],[.025,-.007],[.029,-.003],[.028,.004],[.017,.004],[.016,-.007]],1.65,.087,-.16,trim,32);
 jack.rotation.z=-Math.PI/2;
 cylinder(root,.016,.004,1.639,.087,-.16,dark,24).rotation.z=Math.PI/2;
 // Recessed hinge, rear exhaust and underside intake.
 for(const x of [-1.01,1.01])cylinder(root,.035,.45,x,.137,-1.038,trim,36).rotation.z=Math.PI/2;
 cylinder(root,.030,1.58,0,.137,-1.039,edge,36).rotation.z=Math.PI/2;
 const exhaust=roundedRect(2.51,.043,.014);
 for(let i=0;i<42;i++)exhaust.holes.push(roundedRect(.034,.019,.007,(i-20.5)*.055,0,new THREE.Path()));
 panel(root,exhaust,.010,0,.086,-1.094,edge,0,4).rotation.y=Math.PI;
 box(root,2.428,.022,.006,0,.086,-1.079,dark,.002);
 for(let i=0;i<42;i++)box(root,.006,.017,.014,(i-20.5)*.055,.086,-1.084,trim).rotation.y=.27;
 for(const x of [-1.23,-.78,.78,1.23]){
  cylinder(root,.035,.022,x,.137,-1.038,aluminum,24).rotation.z=Math.PI/2;
  cylinder(root,.031,.024,x,.137,-1.038,edge,22).rotation.z=Math.PI/2;
 }
 box(root,.44,.012,.013,0,.105,1.085,trim,.004);
 lid=new THREE.Group();lid.position.set(0,.143,-1.039);lid.rotation.x=-.19;root.add(lid);
 const eyeY=display(lid,3.31,2.06,0,1.03,0,true);
 marker('显示器中心',lid,0,1.03,.04);marker('扬声器',root,1.53,.137,-.4);marker('摄像头',lid,0,eyeY,.030);marker('键盘',root,0,.145,-.35);marker('散热',root,0,.138,-.80);
 marker('显示器',lid,-.65,1.22,.04);marker('处理器',root,-.72,.149,-.44);
 marker('内存',root,.82,.149,-.44);marker('显卡',root,.48,.149,-.06);
 marker('硬盘',root,-1.1,.138,.5);marker('电池',root,.92,.138,.66);
 marker('主板',root,0,.149,-.1);marker('BIOS',root,.1,.149,-.4);marker('系统',lid,.7,.5,.04);marker('NPU',root,-.4,.149,-.5);marker('声卡',root,1.65,.087,-.16);marker('网卡',root,1.6,.087,-.35);
 batchStatic(root);batchStatic(lid);
 camera.position.set(4.3,3.15,6.5);controls.target.set(0,.96,0);camera.lookAt(controls.target);return root;
}
function desktop(){
 const root=new THREE.Group(),monitor=new THREE.Group();
 monitor.position.set(-1.32,0,-.08);root.add(monitor);
 plate(monitor,.94,.63,.098,.031,0,.035,.015,edge,.009,11);
 plate(monitor,.81,.49,.078,.014,0,.020,.015,keys,.005,10);
 const stand=roundedRect(.149,1.355,.041);
 stand.holes.push(roundedRect(.067,.192,.031,0,-.294,new THREE.Path()));
 panel(monitor,stand,.133,0,.747,-.219,aluminum,.006,7);
 box(monitor,.140,.373,.025,0,1.092,-.293,trim,.009);
 box(monitor,.325,.243,.095,0,1.403,-.228,edge,.041);
 cylinder(monitor,.047,.237,0,1.411,-.258,trim,24).rotation.z=Math.PI/2;
 const eyeY=display(monitor,2.64,1.512,0,1.62,-.08);
 marker('显示器',monitor,-.45,1.67,.02);
 marker('显示器中心',monitor,0,1.62,-.026);marker('摄像头',monitor,0,eyeY,.021);marker('键盘',root,-1.03,.1,1.04);
 batchStatic(monitor);

 const tower=new THREE.Group();tower.position.set(1.38,0,-.17);root.add(tower);
 const rear=roundedRect(1.034,2.288,.044);
 roundHole(rear,.306,-.130,1.837-1.236);
 rear.holes.push(roundedRect(.155,.770,.017,.357,1.698-1.236,new THREE.Path()));
 rear.holes.push(roundedRect(.455,.177,.011,-.132,1.110-1.236,new THREE.Path()));
 rear.holes.push(roundedRect(.559,.251,.019,-.037,.253-1.236,new THREE.Path()));
 for(let i=0;i<6;i++)rear.holes.push(roundedRect(.451,.039,.006,-.132,.663+i*.060-1.236,new THREE.Path()));
 panel(tower,rear,.048,0,1.236,-.765,edge,0,7);

 const roof=roundedRect(1.034,1.57,.066);
 for(let i=0;i<16;i++)for(const row of [-1,1])
  roof.holes.push(roundedRect(.019,.295,.0095,-.352+i*.047,.175+row*.16,new THREE.Path()));
 const cap=panel(tower,roof,.033,0,2.406,0,edge,.004,8);cap.rotation.x=-Math.PI/2;
 box(tower,1.05,.075,1.58,0,.079,0,edge,.029);
 box(tower,.055,2.31,1.54,.492,1.235,0,edge,.023);
 for(const z of [-.74,.741])box(tower,.06,2.26,.092,-.499,1.234,z,edge,.026);
 for(const y of [.148,2.324])box(tower,.055,.084,1.48,-.496,y,0,edge,.026);
 box(tower,.019,2.096,1.38,-.532,1.234,0,new THREE.MeshPhysicalMaterial({color:0x7bacca,roughness:.24,transparent:true,opacity:.11,depthWrite:false}),.006);
 for(const y of [.216,2.25])for(const z of [-.64,.64]){
  cylinder(tower,.029,.006,-.543,y,z,keys,24).rotation.z=Math.PI/2;
  cylinder(tower,.019,.011,-.550,y,z,trim,20).rotation.z=Math.PI/2;
  cylinder(tower,.012,.005,-.557,y,z,aluminum,18).rotation.z=Math.PI/2;
  for(const a of [0,Math.PI/3,Math.PI*2/3])box(tower,.001,.015,.003,-.560,y,z,dark).rotation.x=a;
 }
 desktopInterior(tower,{edge,trim,dark,aluminum,portBlue,accent,fanBlade},internalRotors,fanTrailMaterials);
 for(const x of [-.352,.352])for(const z of [-.552,.552]){
  plate(tower,.174,.196,.042,.016,x,.041,z,trim,.004,9);
  plate(tower,.151,.174,.032,.070,x,-.001,z,keys,.008,8);
  plate(tower,.137,.157,.025,.006,x,-.039,z,dark,.001,7);
  for(let i=0;i<7;i++)box(tower,.105,.003,.006,x,-.042,z+(i-3)*.019,edge);
 }
 for(let i=0;i<22;i++)box(tower,.68,.005,.005,0,2.382,-.486+i*.028,trim,.001);
 for(const [x,w,d,isUsbC] of [[.025,.080,.041,false],[.135,.080,.041,false],[.232,.058,.030,true]]){
  const rim=roundedRect(w,d,isUsbC ? .014 : .006);
  rim.holes.push(roundedRect(w-.013,d-.010,isUsbC ? .010 : .004,0,0,new THREE.Path()));
  panel(tower,rim,.006,x,2.432,.52,trim,0,5).rotation.x=-Math.PI/2;
  box(tower,w-.016,.003,d-.012,x,2.427,.52,dark);
  box(tower,w*.65,.003,d*.28,x,2.430,.52,isUsbC?edge:portBlue);
 }
 cylinder(tower,.055,.011,.370,2.433,.52,trim,30);
 cylinder(tower,.043,.012,.370,2.437,.52,edge,28);
 cylinder(tower,.015,.002,.370,2.444,.52,accent,20);
 turned(tower,[[.010,0],[.018,0],[.018,.006],[.011,.006],[.010,0]],-.082,2.428,.52,trim,20);
 // The fascia and each fan frame are perforated all the way through.
 const fascia=roundedRect(1.02,2.268,.052);
 for(let i=0;i<3;i++)roundHole(fascia,.317,0,.604+i*.65-1.237);
 panel(tower,fascia,.044,0,1.237,.806,edge,.006,16);
 const screenTint=new THREE.MeshPhysicalMaterial({color:0x6d94aa,roughness:.26,transparent:true,opacity:.055,depthWrite:false});
 const bladeGeometry=impeller(.274,.022,7);
 for(let i=0;i<3;i++){
  const y=.604+i*.65,z=.858;
  const mount=roundedRect(.641,.609,.037);
  roundHole(mount,.280);
  for(const dx of [-.276,.276])for(const dy of [-.260,.260])roundHole(mount,.014,dx,dy);
  panel(tower,mount,.053,0,y,z-.035,trim,.002,18);
  const filter=new THREE.Mesh(new THREE.CircleGeometry(.280,72),screenTint);filter.position.set(0,y,z-.092);tower.add(filter);
  // The motor is carried by thin rear struts. No solid backing behind the blades.
  for(let k=0;k<4;k++){
   const a=k*Math.PI/2+Math.PI*.25;
   const arm=box(tower,.245,.017,.016,Math.cos(a)*.161,y+Math.sin(a)*.161,z-.069,edge,.004);
   arm.rotation.z=a;
  }
  cylinder(tower,.060,.026,0,y,z-.064,keys,28).rotation.x=Math.PI/2;
  const ring=new THREE.Mesh(new THREE.TorusGeometry(.291,.011,10,64),accent);
  ring.position.set(0,y,z+.022);tower.add(ring);
  for(const dx of [-.276,.276])for(const dy of [-.260,.260]){
   cylinder(tower,.013,.013,dx,y+dy,z-.003,aluminum,18).rotation.x=Math.PI/2;
   const screw=box(tower,.013,.003,.002,dx,y+dy,z+.0045,dark);screw.rotation.z=dx*3+dy;
  }
  const rotor=new THREE.Group();rotor.position.set(0,y,z+.024);tower.add(rotor);rotors.push(rotor);
  rotor.add(new THREE.Mesh(bladeGeometry.clone(),fanBlade));
  cylinder(rotor,.072,.035,0,0,.004,edge,30).rotation.x=Math.PI/2;
  cylinder(rotor,.044,.007,0,0,.024,trim,28).rotation.x=Math.PI/2;
  const trailMaterial=new THREE.MeshBasicMaterial({color:0xd8e4ed,transparent:true,opacity:0,depthWrite:false});fanTrailMaterials.push(trailMaterial);
  for(const lag of [.12,.24]){const ghost=new THREE.Mesh(bladeGeometry.clone(),trailMaterial);ghost.rotation.z=lag;ghost.position.z=.003;rotor.add(ghost);}
  batchStatic(rotor);
 }
 bladeGeometry.dispose();
 desktopKeyboard(root);
 desktopMouse(root,.46,1.10,{edge,keys,dark,aluminum,trim,lens,accent});
 batchStatic(root);batchStatic(tower);
 marker('处理器',tower,.066,1.731,-.190);marker('内存',tower,.201,1.746,.278);
 marker('显卡',tower,-.389,1.152,.15);marker('硬盘',tower,.315,1.331,.154);marker('主板',tower,.370,1.31,-.20);marker('BIOS',tower,.352,.806,.388);marker('系统',monitor,.7,1.15,.02);marker('NPU',tower,.2,1.7,.05);marker('声卡',tower,.25,2.44,.52);marker('网卡',tower,-.5,1.8,-.7);
 marker('散热',tower,0,1.254,.883);
 camera.position.set(-4.7,3.25,6.6);controls.target.set(0,1.1,.15);camera.lookAt(controls.target);return root;
}
async function render(next={}){
 completionActive=false;completionEffect.stop();
 setComputerOpacity(1);
 computerFadeMotion=null;
 ++keyboardRequest;keyboardMotion=null;keyboardActive=false;keyboardBlend=0;controls.enabled=true;
 if(keyboardScene){keyboardScene.setActive(false);keyboardScene.scene.visible=false;}
 stressEffect.setActive(false);
 componentFocus.close(true);
 config=next;paused=false;opening=false;rotors.length=0;internalRotors.length=0;fanTrailMaterials.length=0;fanSpeed=4;lastAnchors="";resize();toolbar.style.display=config.embedded?'none':'flex';
 if(model){
  scene.remove(model);
  const materials=new Set();model.traverse(o=>{o.geometry?.dispose();if(o.isMesh&&o.material!==screenMaterial)materials.add(o.material);});
  for(const material of materials){if(material.userData.ownsMap)material.map.dispose();material.dispose();}
 }
 screenMaterial?.map?.dispose();screenMaterial?.dispose();markers={};lid=null;
 let texture;
 if(config.wallpaper){try{texture=await new THREE.TextureLoader().loadAsync(config.wallpaper);texture.colorSpace=THREE.SRGBColorSpace;}catch{config.wallpaper=null;}}
 if(!texture){const c=document.createElement('canvas');c.width=512;c.height=320;const c2=c.getContext('2d');const g=c2.createLinearGradient(0,320,512,0);g.addColorStop(0,'#102349');g.addColorStop(1,'#5395c9');c2.fillStyle=g;c2.fillRect(0,0,512,320);texture=new THREE.CanvasTexture(c);texture.colorSpace=THREE.SRGBColorSpace;}
 screenMaterial=new THREE.MeshBasicMaterial({map:texture,toneMapped:false,transparent:true,opacity:0});
 wireMaterial.color.set(config.dark===false?0x315776:0x94b9d5);
 model=config.laptop===false?desktop():laptop();scene.add(model);
 inspectionLabel=null;inspectionMotion=null;
 if(config.inspection)fitInspectionOverview();
 reducedMotion=!!config.reducedMotion||matchMedia('(prefers-reduced-motion: reduce)').matches;
 opening=true;openingStart=performance.now();if(lid)lid.rotation.x=Math.PI/2;controls.update();requestFrame();
}
function inspectionOverviewPose(){
 if(!model)return;
 const sphere=new THREE.Box3().setFromObject(model).getBoundingSphere(new THREE.Sphere());
 const angle=Math.min(THREE.MathUtils.degToRad(camera.fov),2*Math.atan(Math.tan(THREE.MathUtils.degToRad(camera.fov)/2)*camera.aspect));
 const distance=sphere.radius/Math.sin(angle/2)*.94;
 const direction=new THREE.Vector3(config.laptop===false?-.48:.48,.34,.81).normalize();
 return {position:sphere.center.clone().addScaledVector(direction,distance),target:sphere.center.clone()};
}
function fitInspectionOverview(){
 const pose=inspectionOverviewPose();if(!pose)return;
 controls.target.copy(pose.target);camera.position.copy(pose.position);camera.lookAt(controls.target);
 inspectionOverview=pose;
}
function focusCompletionOverview(){
 const pose=inspectionOverviewPose();if(!pose)return;
 inspectionOverview=pose;
 inspectionMotion={start:performance.now(),from:camera.position.clone(),to:pose.position,fromTarget:controls.target.clone(),target:pose.target};
 requestFrame();
}
function inspectionFocus(message){
 // Inspection only moves the camera over the original computer; never load component assets.
 inspectionMotion=null;autoRotate=false;
 if(opening){if(lid)lid.rotation.x=-.19;model.rotation.y=0;screenMaterial.opacity=1;opening=false;}
 scene.updateMatrixWorld(true);
 inspectionLabel=message.label;
 const overview=inspectionOverview;
 if(!overview)return;
 let target,to;
 if(!message.label){target=overview.target.clone();to=overview.position.clone();}
 else{
  const label=['显示器','系统','烤机'].includes(message.label)?'显示器中心'
   :message.label==='声卡'&&config.laptop!==false?'扬声器':message.label;
  const marker=markers[label]||markers['主板'];
  target=marker.getWorldPosition(new THREE.Vector3());
  if(message.label==='烤机')target.lerp(markers['散热'].getWorldPosition(new THREE.Vector3()),config.laptop===false?.5:.28);
  const front=['摄像头','显示器','系统','烤机'].includes(message.label);
  const keyboard=message.label==='键盘';
  let distance=message.label==='烤机'?(config.laptop===false?7.4:5.4):message.label==='摄像头'?1.1:keyboard?4:front?4.5:message.label==='主板'?3.5:2.8;
  // Keep a useful area visible even in the narrower scene pane on a small laptop.
  distance*=Math.max(1,.85/camera.aspect);
  const direction=front?new THREE.Vector3(.06,.12,1)
   :keyboard||config.laptop!==false?new THREE.Vector3(.12,1,.68):new THREE.Vector3(-1,.32,.45);
  if(message.label==='散热'&&config.laptop===false)direction.set(.1,.2,1);
  to=target.clone().addScaledVector(direction.normalize(),distance);
 }
 // Retarget from the current pose, including a partially completed previous transition.
 inspectionMotion={start:performance.now(),from:camera.position.clone(),to,fromTarget:controls.target.clone(),target};
 requestFrame();
}
async function prepareKeyboard(){
 if(keyboardPreparation)return keyboardPreparation;
 keyboardPreparation=(async()=>{
  // Keep the keyboard's original lighting, but no full-scene colored floor.
  keyboardScene=createKeyboardScene({renderer,invalidate:requestFrame,offset:3.6,scale:.18,floor:false,post:message=>window.chrome?.webview?.postMessage(message)});
  scene.add(keyboardScene.scene);keyboardScene.scene.visible=false;keyboardScene.setActive(false);
  renderer.shadowMap.enabled=true;renderer.shadowMap.type=THREE.PCFSoftShadowMap;renderer.shadowMap.autoUpdate=false;
  const textures=new Set();keyboardScene.scene.traverse(o=>{if(o.isMesh&&o.material.map)textures.add(o.material.map);});
  for(const texture of textures)renderer.initTexture(texture);
  keyboardWarming=true;keyboardScene.scene.visible=true;
  const fadeVariants=new Map();
  try{
   await renderer.compileAsync(scene,camera);
   model.traverse(object=>{
    for(const material of object.material?(Array.isArray(object.material)?object.material:[object.material]):[]){
     if(fadeVariants.has(material))continue;
     fadeVariants.set(material,material.transparent);
     if(!material.transparent){material.transparent=true;material.needsUpdate=true;}
    }
   });
   await renderer.compileAsync(scene,camera);
  }finally{
   for(const [material,transparent] of fadeVariants)if(material.transparent!==transparent){material.transparent=transparent;material.needsUpdate=true;}
   keyboardWarming=false;keyboardScene.scene.visible=false;
  }
  // Upload vertex buffers and build the shadow map offscreen before starting the motion.
  const warmup=new THREE.WebGLRenderTarget(8,8),previous=renderer.getRenderTarget();
  const visible=model.visible;model.visible=false;keyboardScene.scene.visible=true;
  try{renderer.setRenderTarget(warmup);renderer.render(scene,keyboardScene.camera);}
  finally{renderer.setRenderTarget(previous);warmup.dispose();model.visible=visible;keyboardScene.scene.visible=false;keyboardScene.setLightWeight(0);}
  requestFrame();
 })();
 return keyboardPreparation;
}
function activateKeyboard(active,width){
 keyboardActive=active;controls.enabled=!active;
 keyboardScene.setActive(active);keyboardScene.scene.visible=true;
 keyboardMotion={from:keyboardBlend,to:active?1:0,start:performance.now()};
 computerFadeMotion={from:computerOpacity,to:active?0:1,start:performance.now()+(active?150:0),duration:active?1250:850};
 lockViewportBuffer(width,keyboardTransitionMs+250);
 if(active){keyboardScene.receive({type:'theme',dark:config.dark});window.chrome?.webview?.postMessage({type:'keyboardReady'});}
 requestFrame();
}
function lockViewportBuffer(width,duration){
 bufferLockUntil=performance.now()+duration;
 bufferWidth=Math.max(bufferWidth,Math.ceil(Number(width)||innerWidth));
 renderer.setSize(bufferWidth,bufferHeight,false);resize();
 clearTimeout(resizeTimer);resizeTimer=setTimeout(resize,duration+50);
}
function receive(message){
 if(message.type==='inspectionComplete'){
  if(!config.inspection||!model)return;
  ++keyboardRequest;keyboardActive=false;keyboardMotion=null;keyboardBlend=0;computerFadeMotion=null;
  setComputerOpacity(1);model.visible=true;controls.enabled=true;
  if(keyboardScene){keyboardScene.setActive(false);keyboardScene.scene.visible=false;}
  stressEffect.setActive(false);completionActive=!!message.active;inspectionLabel=null;autoRotate=false;
  if(completionActive){
   completionEffect.start({dark:config.dark,reduced:reducedMotion||matchMedia('(prefers-reduced-motion: reduce)').matches});
   focusCompletionOverview();
  }else completionEffect.stop();
  requestFrame();return;
 }
 if(message.type==='prepareKeyboard'){
  if(config.inspection)prepareKeyboard().then(()=>{if(message.notify){lockViewportBuffer(message.width,900);window.chrome?.webview?.postMessage({type:'keyboardPrepared'});}}).catch(error=>window.chrome?.webview?.postMessage({type:'keyboardError',message:String(error)}));
  return;
 }
 if(message.type==='keyboardMode'){
  if(!config.inspection||!model)return;
  const request=++keyboardRequest;
  if(message.active)prepareKeyboard().then(()=>{if(request===keyboardRequest)activateKeyboard(true,message.width);}).catch(error=>window.chrome?.webview?.postMessage({type:'keyboardError',message:String(error)}));
  else if(keyboardScene&&(keyboardActive||keyboardBlend>0))activateKeyboard(false,message.width);
  return;
 }
 if(message.type==='keyboard'){if(keyboardActive)keyboardScene?.receive(message.message);return;}
 if(message.type==='stress'){if(config.inspection&&screenMaterial){if(!keyboardActive){computerFadeMotion=null;setComputerOpacity(1);model.visible=true;}stressEffect.setActive(!!message.active);requestFrame();}return;}
 if(message.type==='stressSample'){if(config.inspection)stressEffect.setSample(message);return;}
 if(message.type==='inspectionFocus'){if(config.inspection&&model)inspectionFocus(message);return;}
 if(message.type==='prepareFocus'||message.type==='focus'){
  if(config.inspection)return;
  const prepare=message.type==='prepareFocus'?componentFocus.prepare(message,autoRotate):componentFocus.start(message,autoRotate);
  prepare.catch(err=>window.chrome?.webview?.postMessage({type:'focusFailed',requestId:message.requestId??0,message:err.message}));
  autoRotate=false;return;
 }
 if(message.type==='unfocus'){componentFocus.close(!!message.immediate,message.requestId);return;}
 if(message.type==='pause'){paused=message.value;if(paused){cancelAnimationFrame(frame);frame=0;}else requestFrame();return;}
 if(message.type==='drag'){
 if(componentFocus.active){componentFocus.drag(message.dx,message.dy);return;}
 const offset=camera.position.clone().sub(controls.target),spherical=new THREE.Spherical().setFromVector3(offset);
 spherical.theta-=message.dx*.008;spherical.phi=Math.max(.18,Math.min(Math.PI*.75,spherical.phi-message.dy*.008));
 camera.position.copy(controls.target).add(new THREE.Vector3().setFromSpherical(spherical));controls.update();requestFrame();return;
 }
 if(message.type==='auto'){autoRotate=!!message.value;requestFrame();return;}
 if(message.type==='replay'){opening=true;openingStart=performance.now();requestFrame();return;}
 if(message.type==='layout'){config.connectors=message.connectors;config.starts=message.starts||config.starts;requestFrame();return;}
 if(message.type==='snapshot'){postRender(true);return;}
 if(message.type==='theme'){config.dark=message.dark;completionEffect.dark=!!message.dark;keyboardScene?.receive(message);wireMaterial.color.set(message.dark?0x94b9d5:0x315776);requestFrame();return;}
 render(message).catch(err=>window.chrome?.webview?.postMessage({type:'error',message:err.message}));
}
addEventListener('visibilitychange',()=>{if(document.hidden){cancelAnimationFrame(frame);frame=0;}else requestFrame();});
window.hardwareSnapshot=()=>postRender(true);
window.hardwareSceneState=()=>({opening,lidAngle:lid?.rotation.x,anchors:projectedAnchors(),camera:camera.position.toArray(),target:controls.target.toArray(),screen:{wallpaper:!!config.wallpaper,width:screenMaterial?.map?.image?.width,height:screenMaterial?.map?.image?.height},completion:completionEffect.state(),render:{frame,calls:renderer.info.render.calls,pixelRatio:renderer.getPixelRatio()},inspection:config.inspection?{label:inspectionLabel,moving:!!inspectionMotion,modelVisible:model?.visible,modelOpacity:computerOpacity,modelPosition:model?.position.toArray(),modelScale:model?.scale.toArray()}:null,keyboard:keyboardScene?{active:keyboardActive,sceneVisible:keyboardScene.scene.visible,blend:keyboardBlend,moving:!!keyboardMotion,...keyboardScene.getState()}:null,stress:stressEffect.state(),autoRotate,paused,fanSpeed,fanTrailOpacity:fanTrailMaterials[0]?.opacity??0,fanAngles:rotors.map(r=>r.rotation.z),interiorFanAngles:Object.fromEntries(internalRotors.map(({id,rotor})=>[id,rotor.rotation.z])),focus:componentFocus.state()});
window.receiveHardware=receive;
renderer.domElement.addEventListener('webglcontextlost',e=>{e.preventDefault();window.chrome?.webview?.postMessage({type:'error',message:'WebGL context lost'});});
addEventListener('pagehide',()=>{++keyboardRequest;clearTimeout(resizeTimer);keyboardScene?.dispose();completionEffect.dispose();});
window.renderHardware=render;
window.chrome?.webview?.addEventListener('message',e=>receive(e.data));
const toolbar=document.createElement('div');toolbar.className='toolbar';toolbar.innerHTML='<button id="rotate" aria-pressed="false">自动旋转</button><button id="replay">重播开机</button>';
// Resolve element text from the app language without introducing network dependencies.
document.body.append(toolbar);
toolbar.querySelector('#rotate').onclick=e=>{autoRotate=!autoRotate;e.target.setAttribute('aria-pressed',String(autoRotate));requestFrame();};
toolbar.querySelector('#replay').onclick=()=>{opening=true;openingStart=performance.now();requestFrame();};
window.chrome?.webview?.postMessage({type:'ready'});
if(!window.chrome?.webview){document.body.style.background='#111820';render({laptop:new URLSearchParams(location.search).get('kind')!=='desktop',labels:['处理器','内存','硬盘','显示器','显卡','电池'],connectors:false});}
