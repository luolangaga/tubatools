import * as THREE from 'three';
import { hardwareComponent } from './components.js';
const clamp=t=>Math.max(0,Math.min(1,t));
// Zero velocity and acceleration at both ends, including overlapping motion stages.
const smooth=t=>{t=clamp(t);return t*t*t*(10+t*(-15+6*t));};

export class HardwareFocusController {
 constructor(options) {
  Object.assign(this,options);
  this.current=null;this.pending=null;this.generation=0;this.dragging=null;
  this.canvas.addEventListener('pointerdown',e=>{
   if(this.pending&&!this.current){this.post({type:'dismissFocus',requestId:this.requestId});return;}
   const f=this.current;if(!f||f.closing)return;
   this.dragging={x:e.clientX,y:e.clientY,px:e.clientX,py:e.clientY,moved:false};
   f.auto=false;this.canvas.setPointerCapture(e.pointerId);
   this.post({type:'focusAutoStopped',requestId:f.requestId});
  });
  this.canvas.addEventListener('pointermove',e=>{
   const d=this.dragging;if(!d||!this.current)return;
   const dx=e.clientX-d.px,dy=e.clientY-d.py;
   d.moved ||=Math.hypot(e.clientX-d.x,e.clientY-d.y)>4;
   d.px=e.clientX;d.py=e.clientY;this.drag(dx,dy);
  });
  this.canvas.addEventListener('pointerup',e=>{
   const d=this.dragging;this.dragging=null;const f=this.current;if(!d||!f||f.closing)return;
   if(!d.moved){
    const rect=this.canvas.getBoundingClientRect(),ray=new THREE.Raycaster();
    ray.setFromCamera(new THREE.Vector2((e.clientX-rect.left)/rect.width*2-1,1-(e.clientY-rect.top)/rect.height*2),this.camera);
    if(!ray.intersectObject(f.part,true).length)this.post({type:'dismissFocus',requestId:f.requestId});
   }
  });
  this.canvas.addEventListener('pointercancel',()=>this.dragging=null);
  this.canvas.addEventListener('lostpointercapture',()=>this.dragging=null);
 }
 get active(){return !!(this.current||this.pending);}
 get requestId(){return this.current?.requestId??this.pending?.message.requestId??0;}

 freezeControls() {
  const saved={enabled:this.controls.enabled,damping:this.controls.enableDamping,autoRotate:this.controls.autoRotate};
  const position=this.camera.position.clone(),rotation=this.camera.quaternion.clone(),target=this.controls.target.clone();
  this.controls.enabled=false;this.controls.autoRotate=false;
  // Flush OrbitControls' residual drag without changing the visible camera pose.
  this.controls.enableDamping=false;this.controls.update();
  this.camera.position.copy(position);this.camera.quaternion.copy(rotation);this.controls.target.copy(target);
  this.camera.updateMatrixWorld(true);this.controls.enableDamping=saved.damping;
  return saved;
 }
 restoreControls(saved,autoBefore) {
  this.controls.enabled=saved.enabled;this.controls.enableDamping=saved.damping;
  this.controls.autoRotate=saved.autoRotate;this.restoreAuto(autoBefore);
 }
 prepare(message,autoBefore) {
  if(this.current)return Promise.resolve(null);
  if(this.pending)return this.pending.promise;
  const model=this.getModel();if(!model)return Promise.resolve(null);
  const request={message,autoBefore,model,generation:++this.generation,controls:this.freezeControls()};
  this.pending=request;
  request.promise=(async()=>{
   const geometry=await hardwareComponent(message.label,message.brand,this.getScreenTexture());
   if(this.pending!==request||request.generation!==this.generation)return null;
   // Decode, upload and compile while the computer is stationary, before starting the clock.
   await this.prepareObject(geometry);
   if(this.pending!==request||request.generation!==this.generation)return null;
   const radius=new THREE.Box3().setFromObject(geometry).getBoundingSphere(new THREE.Sphere()).radius;
   return {geometry,radius};
  })();
  return request.promise;
 }
 async start(message,autoBefore) {
  if(this.current)return;
  const loading=this.prepare(message,autoBefore),request=this.pending;
  const asset=await loading;
  if(!asset||!request||this.pending!==request||request.generation!==this.generation||this.getModel()!==request.model)return;
  this.pending=null;
  const model=request.model,x=message.side==='left' ? .75 : .25;
  const position=model.position.clone(),rotation=model.quaternion.clone(),scale=model.scale.clone();
  const part=new THREE.Group();part.add(asset.geometry);part.scale.setScalar(.0001);this.scene.add(part);
  const fans=[];
  asset.geometry.traverse(object=>{if(object.userData.focusSpin)fans.push(object);});
  const center=this.point(.5,.5);
  const f=this.current={label:message.label,requestId:message.requestId??0,model,part,
   position,rotation,scale,targetRotation:rotation.clone().multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0,1,0),message.side==='left'?-.3:.3)),
   horizontal:this.point(x,.5).sub(center),downward:this.point(.5,.78).sub(center),destination:this.point(x,.29),
   up:new THREE.Vector3(0,1,0).applyQuaternion(this.camera.quaternion),origin:new THREE.Vector3(),
   controls:request.controls,autoBefore:request.autoBefore,auto:!this.reduced(),fans,
   yaw:['显示器','系统'].includes(message.label)?-.18:.28,tilt:0,pitch:['显示器','系统'].includes(message.label)?0:1.02,
   euler:new THREE.Euler(),orientation:new THREE.Quaternion(),progress:0,closing:false,
   duration:this.reduced()?0:1040,
   fit:Math.min(1.4,this.point(.69,.3).distanceTo(this.point(.31,.3))/(2*asset.radius),this.point(.5,.08).distanceTo(this.point(.5,.55))/(2*asset.radius))};
  f.start=performance.now();
  this.post({type:'focusStarted',requestId:f.requestId});this.requestFrame();
 }
 point(x,y) {
  const center=this.controls.target.clone().project(this.camera);
  return new THREE.Vector3(x*2-1,1-y*2,center.z).unproject(this.camera);
 }
 markerPosition(f) {
  const marker=this.getMarker(f.label);
  return marker?marker.getWorldPosition(f.origin):f.origin.copy(f.model.position);
 }
 update(now,delta) {
  const f=this.current;if(!f)return false;
  const time=f.duration?clamp((now-f.start)/f.duration):1;
  const progress=smooth(time);
  if(f.closing){
   f.progress=f.closeProgress*(1-progress);
   f.model.position.lerpVectors(f.closePosition,f.position,progress);
   f.model.quaternion.slerpQuaternions(f.closeRotation,f.rotation,progress);
   f.model.scale.lerpVectors(f.closeScale,f.scale,progress);
   f.part.position.lerpVectors(f.closePartPosition,this.markerPosition(f),progress);
   f.part.scale.setScalar(Math.max(.0001,f.closePartScale*(1-progress)));
   if(time===1){this.finish(f);return false;}
   return true;
  }
  f.progress=progress;
  f.model.position.copy(f.position).addScaledVector(f.horizontal,smooth(time/.68)).addScaledVector(f.downward,smooth((time-.2)/.8));
  f.model.quaternion.copy(f.rotation).slerp(f.targetRotation,smooth(time/.9));
  f.model.scale.copy(f.scale).multiplyScalar(1-.22*progress);
  const emergence=smooth((time-.42)/.58);
  f.part.position.copy(this.markerPosition(f)).lerp(f.destination,emergence);
  f.part.position.addScaledVector(f.up,Math.sin(emergence*Math.PI)*.22);
  f.part.scale.setScalar(Math.max(.0001,emergence*f.fit));
  if(f.auto)f.yaw+=delta*.22*smooth((emergence-.35)/.65);
  f.part.quaternion.copy(this.camera.quaternion).multiply(f.orientation.setFromEuler(f.euler.set(f.pitch+f.tilt,f.yaw,0)));
  const spinning=f.fans.length>0&&!this.reduced();
  if(spinning)for(const fan of f.fans)fan.rotateZ(fan.userData.focusSpin*delta);
  return time<1||f.auto||spinning;
 }
 drag(dx,dy) {
  const f=this.current;if(!f||f.closing)return;
  if(f.auto)this.post({type:'focusAutoStopped',requestId:f.requestId});
  f.auto=false;f.yaw+=dx*.009;f.tilt=THREE.MathUtils.clamp(f.tilt+dy*.008,-1.2,1.2);this.requestFrame();
 }
 close(immediate=false,requestId) {
  if(requestId!==undefined&&requestId!==this.requestId)return;
  this.dragging=null;
  if(this.pending){
   const request=this.pending;this.pending=null;this.generation++;
   this.restoreControls(request.controls,request.autoBefore);
   this.post({type:'focusClosed',requestId:request.message.requestId??0});this.requestFrame();return;
  }
  const f=this.current;if(!f)return;
  if(immediate){this.finish(f);this.requestFrame();return;}
  if(f.closing)return;
  this.generation++;
  // Reverse from the pose currently on screen, not from a reconstructed easing parameter.
  f.closePosition=f.model.position.clone();f.closeRotation=f.model.quaternion.clone();f.closeScale=f.model.scale.clone();
  f.closePartPosition=f.part.position.clone();f.closePartScale=f.part.scale.x;f.closeProgress=f.progress;
  f.closing=true;f.auto=false;f.duration=this.reduced()?0:680;f.start=performance.now();this.requestFrame();
 }
 finish(f) {
  this.scene.remove(f.part);f.model.position.copy(f.position);f.model.quaternion.copy(f.rotation);f.model.scale.copy(f.scale);
  this.current=null;this.restoreControls(f.controls,f.autoBefore);
  this.post({type:'focusClosed',requestId:f.requestId});
 }
 state() {
  if(!this.current)return this.pending?{label:this.pending.message.label,preparing:true}:null;
  const f=this.current,materials=[];
  f.part.traverse(n=>{if(n.isMesh)for(const m of Array.isArray(n.material)?n.material:[n.material]){
   if(!materials.some(v=>v.name===m.name))materials.push({name:m.name,type:m.type,color:m.color?.getHexString(),metalness:m.metalness,roughness:m.roughness,map:m.map?.image?{width:m.map.image.width,height:m.map.image.height}:null});
  }});
  return {label:f.label,requestId:f.requestId,progress:f.progress,autoRotate:f.auto,closing:f.closing,partVisible:true,asset:f.part.children[0]?.userData,rotation:f.part.rotation.toArray(),modelPosition:f.model.position.toArray(),fanPose:f.fans.map(fan=>fan.quaternion.toArray()),materials};
 }
}
