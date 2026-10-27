import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
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
 if(!completionActive&&(!config.inspection||!reducedMotion))for(const rotor of rotors)rotor.rotation.z-=delta*fanSpeed;
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
function box(parent,w,h,d,x,y,z,material,r=0.025){
 const mesh=new THREE.Mesh(new RoundedBoxGeometry(w,h,d,3,Math.min(r,w/3,h/3,d/3)),material);
 mesh.position.set(x,y,z);parent.add(mesh);return mesh;
}
function plane(parent,w,h,x,y,z,mat){const m=new THREE.Mesh(new THREE.PlaneGeometry(w,h),mat);m.position.set(x,y,z);parent.add(m);return m;}
function marker(name,parent,x,y,z){const o=new THREE.Object3D();o.position.set(x,y,z);parent.add(o);markers[name]=o;}
function display(parent,w,h,x,y,z){
 box(parent,w,h,0.065,x,y,z,aluminum,0.045);
 box(parent,w-0.035,h-0.035,0.018,x,y,z+0.041,dark,0.035);
 plane(parent,w-0.13,h-0.14,x,y-0.005,z+0.052,screenMaterial);
 const webcam=new THREE.Mesh(new THREE.SphereGeometry(0.007,12,8),new THREE.MeshStandardMaterial({color:0x223b56,metalness:0.6,roughness:0.12}));webcam.position.set(x,y+h/2-0.027,z+0.054);parent.add(webcam);
}
function laptop(){
 const root=new THREE.Group();
 box(root,3.32,0.12,2.2,0,0.06,0,aluminum,0.08);
 box(root,3.3,0.018,2.15,0,0.13,0,aluminum,0.07);
 box(root,2.88,0.012,1.04,0,0.144,-0.33,edge,0.04);
 const legends=document.createElement('canvas');legends.width=1024;legends.height=512;const ctx=legends.getContext('2d');ctx.clearRect(0,0,1024,512);
 const rows=['1234567890−=','QWERTYUIOP[]','ASDFGHJKL;\'','ZXCVBNM,./'];
 ctx.textAlign='center';ctx.textBaseline='middle';
 const keytexture=new THREE.CanvasTexture(legends);keytexture.colorSpace=THREE.SRGBColorSpace;
 keytexture.anisotropy=Math.min(16,renderer.capabilities.getMaxAnisotropy());
 const legendMaterial=new THREE.MeshBasicMaterial({map:keytexture,transparent:true,toneMapped:false,depthWrite:false});
 for(let row=0;row<5;row++)for(let col=0;col<14;col++){
  const x=-1.32+col*0.202,z=-0.74+row*0.194;
  box(root,0.178,0.025,0.164,x,0.163,z,keys,0.018);
  const text=rows[row-1]?.[col-1]|| (row===0?['esc','F1','F2','F3','F4','F5','F6','F7','F8','F9','F10','F11','F12','⌫'][col]:'');
  ctx.fillStyle='#dce8f2';ctx.font=`${text.length>2?22:28}px sans-serif`;ctx.fillText(text,col*64+32,row*64+32,60);
  // Each legend uses the key's own origin and UV cell. A keyboard-wide overlay
  // drifts because its canvas margins/spacing do not match the cap geometry.
  const geometry=new THREE.PlaneGeometry(.15,.14),uv=geometry.attributes.uv;
  for(let i=0;i<uv.count;i++)uv.setXY(i,(col+uv.getX(i))/16,1-(row+1-uv.getY(i))/8);
  const label=new THREE.Mesh(geometry,legendMaterial);label.rotation.x=-Math.PI/2;label.position.set(x,.176,z);root.add(label);
 }
 box(root,0.98,0.004,0.55,0,0.147,0.61,edge,0.035);
 box(root,0.956,0.006,0.526,0,0.15,0.61,aluminum,0.03);
 for(const side of [-1,1])for(let i=0;i<21;i++)box(root,0.045,0.006,0.014,side*1.53,0.145,-0.8+i*0.052,keys,0.003);
 // Recessed side ports and hinge barrels.
 for(const z of [-0.58,-0.25,0.13])box(root,0.008,0.035,0.13,-1.662,0.065,z,dark,0.006);
 for(const x of [-1.12,1.12])box(root,0.48,0.1,0.1,x,0.14,-1.035,edge,0.04);
 lid=new THREE.Group();lid.position.set(0,0.17,-1.03);lid.rotation.x=-0.19;root.add(lid);
 display(lid,3.3,2.06,0,1.03,0);
 marker('显示器中心',lid,0,1.03,.07);marker('扬声器',root,1.53,.16,-.4);marker('摄像头',lid,0,2.033,.054);marker('键盘',root,0,.18,-.35);marker('散热',root,0,.18,-.7);
 marker('显示器',lid,-0.65,1.22,0.07); marker('处理器',root,-0.72,0.18,-0.44);
 marker('内存',root,0.82,0.18,-0.44);marker('显卡',root,0.48,0.18,-0.06);
 marker('硬盘',root,-1.1,0.16,0.5);marker('电池',root,0.92,0.16,0.66);
 marker('主板',root,0,0.18,-0.1);marker('BIOS',root,.1,.18,-.4);marker('系统',lid,.7,.5,.07);marker('NPU',root,-.4,.18,-.5);marker('声卡',root,-1.65,.08,.3);marker('网卡',root,1.6,.08,-.35);
 camera.position.set(4.3,3.15,6.5);controls.target.set(0,0.96,0);camera.lookAt(controls.target);return root;
}
function desktop(){
 const root=new THREE.Group();
 const monitor=new THREE.Group();monitor.position.set(-1.05,0,0.2);root.add(monitor);
 box(monitor,0.95,0.06,0.64,0,0.05,0.03,edge,0.04);
 box(monitor,0.15,0.78,0.13,0,0.45,-0.1,aluminum,0.04);
 display(monitor,2.64,1.66,0,1.62,-0.08);
 marker('显示器',monitor,-0.45,1.67,0.02);
 marker('显示器中心',monitor,0,1.62,-.026);marker('摄像头',monitor,0,2.423,-.026);marker('键盘',root,-1.03,.1,1.04);
 const tower=new THREE.Group();tower.position.set(1.38,0,-0.17);root.add(tower);
 box(tower,1.05,2.38,.08,0,1.23,-.76,edge,.025);
 box(tower,1.05,.08,1.6,0,2.4,0,edge,.025);
 box(tower,1.05,.08,1.6,0,.08,0,edge,.025);
 box(tower,.08,2.38,1.6,.49,1.23,0,edge,.025);
 // Open glass side reveals board, graphics card and memory.
 box(tower,0.035,2.12,1.34,-0.538,1.25,0,new THREE.MeshPhysicalMaterial({color:0x7bacca,metalness:0,roughness:0.1,transparent:true,opacity:0.13,depthWrite:false}),0.01);
 box(tower,0.065,1.58,1.15,-0.37,1.43,-0.04,dark,0.02);
 box(tower,0.12,0.47,0.47,-0.29,1.7,-0.07,aluminum,0.025);
 box(tower,0.06,0.39,0.39,-0.22,1.7,-0.07,edge,0.02);
 for(let i=0;i<2;i++)box(tower,0.06,0.66,0.06,-0.21,1.66,0.35+i*0.1,accent,0.01);
 box(tower,0.62,0.19,1.1,-0.09,1.08,0.04,edge,0.03);
 box(tower,0.64,0.027,1.08,-0.09,1.2,0.04,accent,0.01);
 box(tower,0.7,0.38,1.32,-0.04,0.4,0,dark,0.02);
 for(let i=0;i<3;i++){
  const rotor=new THREE.Group();rotor.position.set(0,.65+i*.65,.816);tower.add(rotor);rotors.push(rotor);
  const ring=new THREE.Mesh(new THREE.TorusGeometry(0.32,0.022,12,48),accent);ring.position.set(0,0.65+i*0.65,0.815);tower.add(ring);
  const hub=new THREE.Mesh(new THREE.CylinderGeometry(0.082,0.082,0.025,24),edge);hub.rotation.x=Math.PI/2;hub.position.copy(ring.position);hub.position.z+=0.01;tower.add(hub);
  const trailMaterial=new THREE.MeshBasicMaterial({color:0xd8e4ed,transparent:true,opacity:0,depthWrite:false});fanTrailMaterials.push(trailMaterial);
  for(let b=0;b<7;b++){
   const blade=box(rotor,0.23,0.075,0.012,0,0,0,aluminum,0.025);const a=b*Math.PI*2/7;blade.position.set(Math.cos(a)*0.17,Math.sin(a)*0.17,0);blade.rotation.z=a+0.65;
   // Translucent trailing poses make the faster rotation legible at a modest frame rate.
   for(const lag of [.12,.24]){const ghost=new THREE.Mesh(blade.geometry,trailMaterial);ghost.position.set(Math.cos(a+lag)*.17,Math.sin(a+lag)*.17,.008);ghost.rotation.z=a+.65+lag;rotor.add(ghost);}
  }
 }
 for(const x of [-0.35,0.35])for(const z of [-0.55,0.55])box(tower,0.12,0.1,0.14,x,0.015,z,keys,0.02);
 box(tower,0.12,0.014,0.05,0.25,2.43,0.4,accent,0.01);
 // Keyboard and mouse complete the desktop silhouette.
 box(root,2.18,0.07,0.68,-1.03,0.05,1.04,aluminum,0.035);
 for(let row=0;row<4;row++)for(let col=0;col<13;col++)box(root,0.135,0.025,0.115,-1.98+col*0.156,0.102,0.81+row*0.146,keys,0.012);
 const mouse=new THREE.Mesh(new THREE.CapsuleGeometry(0.13,0.22,6,16),aluminum);mouse.rotation.x=Math.PI/2;mouse.scale.z=0.5;mouse.position.set(0.47,0.11,1.1);root.add(mouse);
 marker('处理器',tower,-0.22,1.7,-0.07);marker('内存',tower,-0.2,1.66,0.42);
 marker('显卡',tower,-0.08,1.18,0.35);marker('硬盘',tower,-0.3,0.65,0.4);marker('主板',tower,-0.38,1.36,-0.3);marker('BIOS',tower,-.37,1.5,.22);marker('系统',monitor,.7,1.15,.02);marker('NPU',tower,-.25,1.7,.05);marker('声卡',tower,.25,2.44,.45);marker('网卡',tower,-.5,1.8,-.7);
 marker('散热',tower,0,1.3,.82);
 camera.position.set(-4.7,3.25,6.6);controls.target.set(0,1.1,0.15);camera.lookAt(controls.target);return root;
}
function lineModel(root){
 const meshes=[];root.traverse(o=>{if(o.isMesh&&o.material!==screenMaterial)meshes.push(o);});
 // Keep solid, lightly shaded forms. Outlines only reinforce the silhouette.
 for(const mesh of meshes){
  if(mesh.geometry.type!=='RoundedBoxGeometry'||mesh.material===keys)continue;
  mesh.geometry.computeBoundingBox();const size=mesh.geometry.boundingBox.getSize(new THREE.Vector3());
  if(Math.max(size.x,size.y,size.z)<.5)continue;
  const outline=new THREE.LineSegments(new THREE.EdgesGeometry(new THREE.BoxGeometry(size.x,size.y,size.z)),
   new THREE.LineBasicMaterial({color:0x9db5c8,transparent:true,opacity:.35}));
  mesh.add(outline);
 }
}
async function render(next={}){
 completionActive=false;completionEffect.stop();
 setComputerOpacity(1);
 computerFadeMotion=null;
 ++keyboardRequest;keyboardMotion=null;keyboardActive=false;keyboardBlend=0;controls.enabled=true;
 if(keyboardScene){keyboardScene.setActive(false);keyboardScene.scene.visible=false;}
 stressEffect.setActive(false);
 componentFocus.close(true);
 config=next;paused=false;opening=false;rotors.length=0;fanTrailMaterials.length=0;fanSpeed=4;lastAnchors="";resize();toolbar.style.display=config.embedded?'none':'flex';
 if(model){scene.remove(model);model.traverse(o=>{o.geometry?.dispose();if(o.isMesh&&o.material!==screenMaterial)o.material.dispose();});}
 screenMaterial?.map?.dispose();screenMaterial?.dispose();markers={};lid=null;
 let texture;
 if(config.wallpaper){try{texture=await new THREE.TextureLoader().loadAsync(config.wallpaper);texture.colorSpace=THREE.SRGBColorSpace;}catch{config.wallpaper=null;}}
 if(!texture){const c=document.createElement('canvas');c.width=512;c.height=320;const c2=c.getContext('2d');const g=c2.createLinearGradient(0,320,512,0);g.addColorStop(0,'#102349');g.addColorStop(1,'#5395c9');c2.fillStyle=g;c2.fillRect(0,0,512,320);texture=new THREE.CanvasTexture(c);texture.colorSpace=THREE.SRGBColorSpace;}
 screenMaterial=new THREE.MeshBasicMaterial({map:texture,toneMapped:false,transparent:true,opacity:0});
 wireMaterial.color.set(config.dark===false?0x315776:0x94b9d5);
 model=config.laptop===false?desktop():laptop();lineModel(model);scene.add(model);
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
window.hardwareSceneState=()=>({opening,lidAngle:lid?.rotation.x,anchors:projectedAnchors(),camera:camera.position.toArray(),target:controls.target.toArray(),screen:{wallpaper:!!config.wallpaper,width:screenMaterial?.map?.image?.width,height:screenMaterial?.map?.image?.height},completion:completionEffect.state(),render:{frame,calls:renderer.info.render.calls,pixelRatio:renderer.getPixelRatio()},inspection:config.inspection?{label:inspectionLabel,moving:!!inspectionMotion,modelVisible:model?.visible,modelOpacity:computerOpacity,modelPosition:model?.position.toArray(),modelScale:model?.scale.toArray()}:null,keyboard:keyboardScene?{active:keyboardActive,sceneVisible:keyboardScene.scene.visible,blend:keyboardBlend,moving:!!keyboardMotion,...keyboardScene.getState()}:null,stress:stressEffect.state(),autoRotate,paused,fanSpeed,fanTrailOpacity:fanTrailMaterials[0]?.opacity??0,fanAngles:rotors.map(r=>r.rotation.z),focus:componentFocus.state()});
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
