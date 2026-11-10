import * as THREE from 'three';
import { box as block, cylinder, batchStatic, impeller, panel, plate, roundedRect, roundHole, turned, cable } from './geometry.js';
import { monitorBack } from './monitor-back.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
const cache=new Map();
const loader=new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
function material(color,metalness=.15,roughness=.5){return new THREE.MeshStandardMaterial({color,metalness,roughness});}
function label(g,text,w,h,x,y,z,color='#d8e8ed'){
 const c=document.createElement('canvas');c.width=256;c.height=96;
 const ctx=c.getContext('2d');ctx.fillStyle=color;ctx.font='600 58px Segoe UI, sans-serif';ctx.textAlign='center';ctx.textBaseline='middle';
 ctx.fillText(text,128,50,224);
 const map=new THREE.CanvasTexture(c);map.colorSpace=THREE.SRGBColorSpace;
 const p=new THREE.Mesh(new THREE.PlaneGeometry(w,h),new THREE.MeshBasicMaterial({map,transparent:true,depthWrite:false,toneMapped:false}));
 p.rotation.x=-Math.PI/2;p.position.set(x,y,z);g.add(p);
}
function surfaceInk(g,w,h,x,y,z,draw){
 const c=document.createElement('canvas');c.width=512;c.height=256;
 draw(c.getContext('2d'));
 const map=new THREE.CanvasTexture(c);map.colorSpace=THREE.SRGBColorSpace;map.anisotropy=4;
 const mesh=new THREE.Mesh(new THREE.PlaneGeometry(w,h),new THREE.MeshBasicMaterial({map,transparent:true,depthWrite:false,toneMapped:false}));
 mesh.rotation.x=-Math.PI/2;mesh.position.set(x,y,z);g.add(mesh);
}
function outline(g,points,thickness,mat){
 const shape=new THREE.Shape();
 points.forEach(([x,z],i)=>i?shape.lineTo(x,-z):shape.moveTo(x,-z));shape.closePath();
 const mesh=new THREE.Mesh(new THREE.ExtrudeGeometry(shape,{depth:thickness,bevelEnabled:false}),mat);
 mesh.rotation.x=-Math.PI/2;mesh.position.y=-thickness/2;g.add(mesh);return mesh;
}
function fastener(g,x,y,z,r,metal,black){
 turned(g,[[0,-.006],[r*.88,-.006],[r,-.002],[r,.004],[r*.76,.009],[0,.009]],x,y,z,metal,22);
 for(const angle of [.48,.48+Math.PI/2]){
  const slot=block(g,r*1.03,.0015,r*.20,x,y+.0093,z,black);slot.rotation.y=angle;
 }
}
function smds(g,x,y,z,count,step,black,metal){
 for(let i=0;i<count;i++){
  const x0=x+i*step;
  block(g,.056,.029,.066,x0,y,z,black,.004);
  for(const side of [-1,1]){
   block(g,.069,.010,.030,x0,y-.011,z+side*.041,metal,.002);
   block(g,.056,.015,.012,x0,y+.005,z+side*.032,metal,.002);
  }
 }
}
function trace(g,points,y,width,mat){
 for(let i=1;i<points.length;i++){
  const [[ax,az],[bx,bz]]=points.slice(i-1,i+1),dx=bx-ax,dz=bz-az;
  const strip=block(g,Math.hypot(dx,dz)+width,.0015,width,(ax+bx)/2,y,(az+bz)/2,mat);
  strip.rotation.y=-Math.atan2(dz,dx);
 }
}
function solderVia(g,x,y,z,r,metal,black){
 cylinder(g,r,.002,x,y,z,metal,12);
 cylinder(g,r*.42,.002,x,y+.0015,z,black,10);
}
function undersideJoint(g,x,y,z,r,metal,gold){
 cylinder(g,r,.003,x,y,z,gold,14);
 turned(g,[[0,-.019],[r*.32,-.016],[r*.55,-.009],[r*.86,-.002],[r*.91,0],[0,0]],x,y,z,metal,12);
}
function gullwing(g,x,y,z,side,w,mat){
 const path=new THREE.Shape();
 [[0,0],[.066,0],[.090,.022],[.112,.086],[.156,.103],[.207,.103],[.207,.119],[.150,.119],
  [.097,.099],[.075,.032],[.058,.015],[0,.015]].forEach(([u,v],i)=>i?path.lineTo(u*side,v):path.moveTo(u*side,v));
 path.closePath();
 const pin=panel(g,path,w,x,y,z,mat,.0013,3);
 pin.rotation.y=Math.PI/2;return pin;
}
function coaxSocket(g,x,y,z,metal,insulator){
 turned(g,[[.043,-.010],[.081,-.010],[.085,.002],[.078,.022],[.066,.026],[.056,.024],[.051,.012],[.043,.010],[.043,-.010]],x,y,z,metal,36);
 cylinder(g,.042,.008,x,y+.004,z,insulator,24);
 cylinder(g,.017,.018,x,y+.013,z,metal,20);
}
function monitorPart(g,screenTexture,m){
 const {metal,black,blue}=m,w=3.90,h=w*1.512/2.64,y=1.864;
 const bezel=material(0x151c26,.30,.41),back=material(0x27313e,.14,.66),edgeGlass=material(0x0d1523,.26,.27);
 panel(g,roundedRect(w,h,.057),.057,0,y,0,bezel,.007,13);
 panel(g,roundedRect(w-.045,h-.034,.042),.008,0,y,.036,edgeGlass,.002,12);
 const aperture=w*2.578/2.64,activeH=aperture*9/16;
 const screenGeometry=new THREE.ShapeGeometry(roundedRect(aperture,activeH,.018),10),p=screenGeometry.attributes.position,uv=screenGeometry.attributes.uv;
 for(let i=0;i<uv.count;i++)uv.setXY(i,p.getX(i)/aperture+.5,p.getY(i)/activeH+.5);
 const screen=new THREE.Mesh(screenGeometry,new THREE.MeshBasicMaterial({map:screenTexture||null,color:screenTexture?0xffffff:0x244d77,toneMapped:false}));
 screen.position.set(0,y+.013,.045);g.add(screen);
 monitorBack(g,w,0,y,0,{shell:back,dark:black,metal,trim:bezel,portBlue:blue});
 const stem=roundedRect(.225,1.650,.049);
 stem.holes.push(roundedRect(.101,.269,.048,0,-.376,new THREE.Path()));
 panel(g,stem,.153,0,.940,-.214,metal,.007,8);
 block(g,.228,.405,.068,0,1.471,-.302,back,.021);
 block(g,.444,.354,.096,0,1.706,-.201,bezel,.026);
 cylinder(g,.065,.325,0,1.705,-.256,metal,30).rotation.z=Math.PI/2;
 plate(g,1.48,.768,.113,.049,0,.087,-.135,bezel,.011,12);
 plate(g,1.34,.657,.092,.015,0,.048,-.135,black,.003,10);
 for(const x of [-.528,.528])plate(g,.118,.415,.049,.006,x,.039,-.133,back,.002,7);
 block(g,.033,.006,.003,1.718,y-h/2+.016,.044,blue,.001);
}
function flashPart(g,m){
 const {pcb,metal,black,gold,traceInk,silk}=m;
 const board=roundedRect(2.59,1.65,.112);
 for(const x of [-1.08,1.08])for(const z of [-.603,.603])roundHole(board,.057,x,z);
 panel(g,board,.077,0,0,0,pcb,.005,10).rotation.x=-Math.PI/2;
 for(const x of [-1.08,1.08])for(const z of [-.603,.603])
  turned(g,[[.057,0],[.083,0],[.083,.004],[.057,.004],[.057,0]],x,.041,z,gold,26);
 for(const side of [-1,1])for(let i=0;i<4;i++){
  const x=-.449+i*.298,z=side*.590;
  plate(g,.143,.185,.018,.009,x,.047,z,gold,.001,6);
  gullwing(g,x,.052,z,side,.085,metal);
  for(const dx of [-.034,.034]){
   const solder=new THREE.Mesh(new THREE.SphereGeometry(.017,10,6),metal);
   solder.scale.set(1.1,.41,1.65);solder.position.set(x+dx,.053,z+side*.021);g.add(solder);
  }
  trace(g,[[x,z+side*.015],[x+(i-1.5)*.073,z+side*.14],[x+(i-1.5)*.073,z+side*.205]],.043,.016,traceInk);
  solderVia(g,x+(i-1.5)*.073,.043,z+side*.205,.018,gold,black);
 }
 plate(g,1.259,.846,.052,.116,0,.194,0,black,.018,12);
 const top=roundedRect(1.145,.73,.059);roundHole(top,.041,-.454,.243);
 panel(g,top,.004,0,.274,0,material(0x293341,.12,.66),0,12).rotation.x=-Math.PI/2;
 cylinder(g,.030,.003,-.454,.269,-.243,black,24);
 label(g,'SPI FLASH',.878,.202,.042,.279,.012,'#abbac7');
 label(g,'SOIC  •  8',.547,.093,.038,.279,.188,'#8392a1');
 smds(g,-1.148,.063,-.246,3,.178,black,metal);smds(g,.720,.063,.316,3,.163,black,metal);
 for(const x of [-.97,.969])for(const z of [-.32,-.16,.02,.18,.34])
  solderVia(g,x,.043,z,.013,metal,black);
 for(const side of [-1,1])for(let i=0;i<3;i++){
  const z=-.387+i*.215;
  trace(g,[[side*.675,z],[side*.752,z],[side*.873,z+.084],[side*1.008,z+.084]],.044,.012,traceInk);
  solderVia(g,side*.674,.044,z,.011,gold,black);
 }
 for(const z of [-.501,.501])block(g,.251,.002,.010,.82,.041,z,silk);
 for(const [x,z] of [[-.866,.402],[.896,-.414]])for(let i=0;i<3;i++){
  cylinder(g,.033,.003,x+(i-1)*.083,.044,z,gold,16);
  cylinder(g,.018,.002,x+(i-1)*.083,.047,z,metal,14);
 }
 for(const side of [-1,1])for(let i=0;i<4;i++){
  const x=-.449+i*.298,z=side*(.605+(i%2)*.082),u=x+(i-1.5)*.083;
  undersideJoint(g,x,-.040,z,.026,metal,gold);
  trace(g,[[x,z],[u,z-side*.117],[u,z-side*.329],[u*.67,z-side*.397]],-.040,.009,traceInk);
  undersideJoint(g,u*.67,-.041,z-side*.397,.015,metal,gold);
 }
 for(const x of [-.84,.84]){
  plate(g,.212,.146,.018,.006,x,-.044,0,gold,.001,5);
  for(const z of [-.039,.039])plate(g,.160,.023,.005,.004,x,-.048,z,metal,0,5);
 }
 label(g,'U1',.254,.103,-.785,.046,-.51,'#9aafa3');
 label(g,'TP',.207,.103,.877,.046,.520,'#9aafa3');
}
function npuPart(g,m){
 const {pcb,metal,black,gold,traceInk,silk,blue}=m;
 plate(g,2.53,2.53,.134,.095,0,0,0,pcb,.008,13);
 plate(g,2.468,2.468,.117,.013,0,-.056,0,material(0x153331,.16,.65),.003,11);
 plate(g,2.098,2.098,.134,.048,0,.080,0,black,.013,13);
 plate(g,1.385,1.225,.046,.057,0,.136,0,metal,.008,12);
 plate(g,1.250,1.094,.047,.040,0,.186,0,blue,.006,12);
 // Flat silicon markings: repeated logic blocks and bus lanes, with no raised tiles on the die.
 surfaceInk(g,1.147,.981,0,.214,0,c=>{
  c.fillStyle='rgba(186,217,224,.34)';c.strokeStyle='rgba(185,220,232,.44)';c.lineWidth=1.2;
  for(let row=0;row<4;row++)for(let col=0;col<6;col++){
   const x=51+col*60,y=35+row*44;c.strokeRect(x,y,52,36);
   for(let i=0;i<5;i++)for(let j=0;j<3;j++)c.fillRect(x+5+i*8,y+4+j*10,5,6);
  }
  c.lineWidth=.85;
  for(let i=0;i<7;i++){
   c.strokeRect(21+i*4,15+i*3,472-i*8,223-i*6);
   c.fillRect(97+i*43,214,36,5);
  }
 });
 for(const z of [-.778,.778]){
  smds(g,-.899,.127,z,12,.163,black,metal);
  block(g,1.869,.002,.015,0,.109,z+Math.sign(z)*.128,silk);
 }
 for(const x of [-.97,.97])for(let i=0;i<9;i++)
  block(g,.086,.019,.035,x,.121,-.625+i*.157,i%3?metal:gold,.003);
 for(const side of [-1,1])for(let i=0;i<21;i++){
  const v=(i-10)*.100;
  trace(g,[[v,side*1.23],[v,side*1.182],[v*.96,side*1.124]],.049,.009,traceInk);
 }
 for(const x of [-1.123,1.123])for(let i=0;i<12;i++)solderVia(g,x,.054,-.89+i*.161,.012,gold,black);
 // Underfill fillet around the exposed die, plus substrate corner registration.
 const fill=roundedRect(1.449,1.286,.052);fill.holes.push(roundedRect(1.37,1.22,.039,0,0,new THREE.Path()));
 panel(g,fill,.012,0,.110,0,black,.001,8).rotation.x=-Math.PI/2;
 for(const x of [-1.122,1.122])for(const z of [-1.122,1.122]){
  const signX=Math.sign(x),signZ=Math.sign(z);
  block(g,.104,.002,.012,x+signX*.016,.053,z+signZ*.084,silk);
  block(g,.012,.002,.105,x+signX*.074,.053,z+signZ*.038,silk);
  solderVia(g,x,.053,z,.021,gold,black);
 }
 const ballGeometry=new THREE.SphereGeometry(.031,9,5),coords=[];
 for(let x=-14;x<=14;x++)for(let z=-14;z<=14;z++)
  if(Math.max(Math.abs(x),Math.abs(z))>=8&&Math.abs(x)+Math.abs(z)<27)coords.push([x*.079,z*.079]);
 const bumps=new THREE.InstancedMesh(ballGeometry,metal,coords.length),matrix=new THREE.Matrix4();
 coords.forEach(([x,z],i)=>bumps.setMatrixAt(i,matrix.makeTranslation(x,-.076,z)));g.add(bumps);
 label(g,'NPU',.596,.165,0,.217,.030,'#dce6ee');
}
function filletedProfile(points,r){
 const shape=new THREE.Shape();
 const mix=(a,b,f)=>[a[0]+(b[0]-a[0])*f,a[1]+(b[1]-a[1])*f];
 for(const [i,p] of points.entries()){
  const a=points[(i+points.length-1)%points.length],b=points[(i+1)%points.length];
  const before=mix(p,a,Math.min(.36,r/Math.hypot(a[0]-p[0],a[1]-p[1])));
  const after=mix(p,b,Math.min(.36,r/Math.hypot(b[0]-p[0],b[1]-p[1])));
  if(i)shape.lineTo(...before);else shape.moveTo(...before);
  shape.quadraticCurveTo(...p,...after);
 }
 shape.closePath();return shape;
}
function am5Lid(scale=1){
 // Two relief pockets per side leave the mounting ears and expose the package passives.
 const side=[[-.825,-.863],[-.635,-.863],[-.594,-.822],[-.594,-.708],[-.335,-.708],[-.335,-.814],
  [.335,-.814],[.335,-.708],[.594,-.708],[.594,-.822],[.635,-.863],[.825,-.863]];
 const points=[];
 for(let i=0;i<4;i++)for(const [x,z] of side){
  const q=i===0?[x,z]:i===1?[-z,x]:i===2?[-x,-z]:[z,-x];
  points.push(q.map(v=>v*scale));
 }
 return filletedProfile(points,.016*scale);
}
function amdEtching(g,shape,y){
 const canvas=document.createElement('canvas');canvas.width=512;canvas.height=512;
 const ctx=canvas.getContext('2d'),finish=ctx.createLinearGradient(0,0,0,512);
 finish.addColorStop(0,'#c8cdd0');finish.addColorStop(.48,'#e3e6e7');finish.addColorStop(1,'#d0d5d9');
 ctx.fillStyle=finish;ctx.fillRect(0,0,512,512);
 let seed=0x12314;
 for(let i=0;i<980;i++){
  seed=(Math.imul(seed,1664525)+1013904223)>>>0;
  const x=(seed&511)-60, yy=(seed>>>9)&511, width=110+((seed>>>18)&383);
  ctx.fillStyle=i%3?'rgba(41,49,58,.045)':'rgba(255,255,255,.09)';ctx.fillRect(x,yy,width,1);
 }
 // Monochrome version of the repository's Assets/Brands/amd.svg, laser etched as on the IHS.
 const logo=new Path2D('M18.324 9.137l1.559 1.56h2.556v2.557L24 14.814V9.137zM2 9.52l-2 4.96h1.309l.37-.982H3.9l.408.982h1.338L3.432 9.52zm4.209 0v4.955h1.238v-3.092l1.338 1.562h.188l1.338-1.556v3.091h1.238V9.52H10.47l-1.592 1.845L7.287 9.52zm6.283 0v4.96h2.057c1.979 0 2.88-1.046 2.88-2.472 0-1.36-.937-2.488-2.747-2.488zm1.237.91h.792c1.17 0 1.63.711 1.63 1.57 0 .728-.372 1.572-1.616 1.572h-.806zm-10.985.273l.791 1.932H2.008zm17.137.307l-1.604 1.603v2.25h2.246l1.604-1.607h-2.246z');
 ctx.fillStyle='#3b444b';ctx.save();ctx.translate(158,183);ctx.scale(8.15,8.15);ctx.translate(0,-9.137);ctx.fill(logo);ctx.restore();
 ctx.font='600 53px Segoe UI, sans-serif';ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillText('RYZEN',251,266,199);
 ctx.fillStyle='#535d65';ctx.font='15px monospace';ctx.textAlign='left';ctx.fillText('AM5  •  LAND GRID ARRAY',105,340);
 for(let row=0;row<8;row++)for(let col=0;col<8;col++){
  if(row===0||col===0||((row*13+col*17+row*col)%7)<3)ctx.fillRect(357+col*4,326+row*4,2.7,2.7);
 }
 const geometry=new THREE.ShapeGeometry(shape,10),p=geometry.attributes.position,uv=geometry.attributes.uv;
 for(let i=0;i<uv.count;i++)uv.setXY(i,p.getX(i)/1.76+.5,p.getY(i)/1.76+.5);
 const map=new THREE.CanvasTexture(canvas);map.colorSpace=THREE.SRGBColorSpace;map.anisotropy=4;
 const mesh=new THREE.Mesh(geometry,new THREE.MeshStandardMaterial({map,metalness:.49,roughness:.43}));
 mesh.rotation.x=-Math.PI/2;mesh.position.y=y;g.add(mesh);
}
function cpuPart(g,brand,m){
 const {pcb,metal,black,gold,traceInk,silk}=m,am=brand==='amd',w=am?2.025:1.93,d=am?2.025:2.17;
 const a=w/2,b=d/2;
 const substrate=filletedProfile([[-a,-b],[a,-b],[a,-.59],[a-.070,-.59],[a-.070,-.44],[a,-.44],[a,b],
  [-a,b],[-a,.44],[-a+.070,.44],[-a+.070,.59],[-a,.59]],.029);
 panel(g,substrate,.077,0,0,0,pcb,.004,8).rotation.x=-Math.PI/2;
 panel(g,substrate,.008,0,-.041,0,material(0x365449,.29,.53),0,8).rotation.x=-Math.PI/2;
 if(am){
  const ledge=material(0x627175,.57,.41);
  const lid=am5Lid(),cut=am5Lid(.987);
  plate(g,1.305,1.305,.082,.030,0,.056,0,black,.005,10);
  panel(g,lid,.044,0,.102,0,ledge,.012,9).rotation.x=-Math.PI/2;
  panel(g,cut,.108,0,.167,0,metal,.014,10).rotation.x=-Math.PI/2;
  amdEtching(g,cut,.236);
  const ceramic=material(0x8a745c,.15,.61);
  // Packages and plated terminations are visible in all eight relief pockets.
  for(const axis of ['x','z'])for(const side of [-1,1])for(const bay of [-1,1])for(let i=0;i<3;i++){
   const u=bay*.465+(i-1)*.064,v=side*(.829+(i%2)*.029);
   const x=axis==='z'?u:v,z=axis==='z'?v:u;
   block(g,axis==='z' ? .047 : .066,.022,axis==='z' ? .066 : .047,x,.061,z,i%2?black:ceramic,.003);
   for(const end of [-1,1]){
    block(g,axis==='z' ? .043 : .015,.012,axis==='z' ? .015 : .043,x+(axis==='x'?end*.031:0),.058,z+(axis==='z'?end*.031:0),metal,.001);
   }
  }
  for(let i=0;i<10;i++)for(const side of [-1,1]){
   const v=-.862+i*.185;
   solderVia(g,v,.042,side*.954,.009,gold,black);
   solderVia(g,side*.955,.042,v,.008,traceInk,black);
  }
 }else{
  plate(g,w*.87,d*.89,.077,.031,0,.055,0,black,.006,11);
  plate(g,1.621,1.863,.158,.052,0,.102,0,metal,.018,14);
  plate(g,1.512,1.756,.144,.104,0,.171,0,metal,.032,14);
  label(g,'PROCESSOR',.867,.163,0,.257,0,'#415467');
  for(const x of [-.779,.779])for(const z of [-.821,.821])solderVia(g,x,.058,z,.015,traceInk,black);
 }
 const pinOne=new THREE.Shape();pinOne.moveTo(0,0);pinOne.lineTo(.100,0);pinOne.lineTo(0,.100);pinOne.closePath();
 panel(g,pinOne,.002,-w/2+.079,.044,d/2-.077,gold).rotation.x=-Math.PI/2;
 const padLayout=am?{count:44,pitchX:.043,pitchZ:.043,r:.0146}:{count:36,pitchX:.050,pitchZ:.056,r:.018};
 const lands=[],{count,pitchX,pitchZ,r}=padLayout;
 for(let row=0;row<count;row++)for(let col=0;col<count;col++){
  const u=col-(count-1)/2,v=row-(count-1)/2;
  if(Math.max(Math.abs(u),Math.abs(v))<(am?6.5:6.0)||Math.abs(u)+Math.abs(v)>count-5)continue;
  if(Math.abs(u)>count*.42&&Math.abs(v+Math.sign(u)*12)<2)continue;
  lands.push([u*pitchX,v*pitchZ]);
 }
 const geometry=new THREE.CircleGeometry(r,9).scale(1,.83,1).rotateX(Math.PI/2);
 const contacts=new THREE.InstancedMesh(geometry,gold,lands.length),matrix=new THREE.Matrix4();
 lands.forEach(([x,z],i)=>contacts.setMatrixAt(i,matrix.makeTranslation(x,-.047,z)));g.add(contacts);
 for(const x of [-.177,0,.177])for(let i=0;i<7;i++){
  const z=-.196+i*.065;
  block(g,.087,.019,.034,x,-.053,z,i%2?black:traceInk,.003);
  for(const side of [-1,1])block(g,.015,.014,.036,x+side*.042,-.053,z,metal,.001);
 }
 // Keep the contact field clear: routing and capacitor solder are confined to the central keep-out.
 for(const side of [-1,1])for(let i=0;i<7;i++){
  const z=-.196+i*.065,x=side*.224;
  trace(g,[[x,z],[x+side*.021,z],[x+side*.036,z+(i<3?-.015:.015)]],-.047,.006,traceInk);
  undersideJoint(g,x+side*.036,-.048,z+(i<3?-.015:.015),.008,metal,gold);
 }
 for(const x of [-.111,.111])for(const z of [-.255,.255]){
  block(g,.056,.004,.020,x,-.048,z,silk,.001);
  block(g,.009,.004,.043,x+Math.sign(x)*.027,-.048,z,silk,.001);
 }
}
function wirelessPart(g,m){
 const {pcb,metal,black,gold,traceInk,silk}=m;
 const profile=new THREE.Shape();
 profile.moveTo(-1.05,-1.43);profile.lineTo(-.17,-1.43);profile.quadraticCurveTo(-.142,-1.28,0,-1.28);
 profile.quadraticCurveTo(.142,-1.28,.17,-1.43);profile.lineTo(1.05,-1.43);profile.quadraticCurveTo(1.10,-1.43,1.10,-1.38);
 for(const [x,z] of [[1.10,1.43],[.502,1.43],[.502,1.211],[.355,1.211],[.355,1.43],[-1.10,1.43],[-1.10,-1.38]])profile.lineTo(x,z);
 profile.quadraticCurveTo(-1.10,-1.43,-1.05,-1.43);profile.closePath();
 // XY silhouette uses the XZ coordinates above directly.
 const board=panel(g,profile,.066,0,0,0,pcb,.003,13);
 board.rotation.x=Math.PI/2;
 for(const face of [-1,1])for(let i=0;i<24;i++){
  const x=-1.001+i*.088;
  if(x>.322&&x<.536)continue;
  plate(g,.054,.197,.008,.003,x,face*.036,1.321,gold,0,5);
  if(face===1)trace(g,[[x,1.218],[x,1.141],[x*.94,1.09]],.035,.008,traceInk);
 }
 const screwEdge=new THREE.Shape();
 screwEdge.moveTo(-.235,1.427);screwEdge.quadraticCurveTo(-.218,1.205,0,1.205);
 screwEdge.quadraticCurveTo(.218,1.205,.235,1.427);screwEdge.lineTo(.17,1.427);
 screwEdge.quadraticCurveTo(.143,1.274,0,1.274);screwEdge.quadraticCurveTo(-.143,1.274,-.17,1.427);
 screwEdge.closePath();
 panel(g,screwEdge,.003,0,.037,0,gold,0,14).rotation.x=-Math.PI/2;
 const shield=material(0x9ca8ad,.58,.39),lip=material(0x526677,.61,.38);
 plate(g,1.904,1.481,.084,.026,0,.052,-.068,lip,.006,12);
 plate(g,1.813,1.386,.079,.092,0,.109,-.066,shield,.015,12);
 const stamping=roundedRect(1.671,1.238,.061);stamping.holes.push(roundedRect(1.588,1.155,.047,0,0,new THREE.Path()));
 panel(g,stamping,.002,0,.171,-.066,metal,0,10).rotation.x=-Math.PI/2;
 for(const x of [-.712,.712]){
  coaxSocket(g,x,.061,-1.039,gold,black);
  solderVia(g,x*.68,.038,-.993,.018,metal,black);
  for(const z of [-.903,-.797])block(g,.052,.002,.010,x,.038,z,silk);
  for(const dz of [-.064,.064])for(const dx of [-.058,.058])block(g,.046,.009,.032,x+dx,.040,-1.039+dz,metal,.002);
 }
 for(const z of [.835,.976])smds(g,-.847,.054,z,9,.211,black,metal);
 for(const x of [-1.024,1.024])for(let i=0;i<10;i++)solderVia(g,x,.036,-.658+i*.154,.015,metal,black);
 for(let i=0;i<12;i++)solderVia(g,-.926+i*.163,.036,-.838,.010,gold,black);
 label(g,'MAIN',.278,.103,-.711,.039,-1.201,'#aabcae');
 label(g,'AUX',.243,.103,.711,.039,-1.201,'#aabcae');
 for(const x of [-.685,.685])for(const z of [-.601,.460])
  cylinder(g,.022,.002,x,.171,z,metal,14);
 for(const x of [-.918,.918])for(let i=0;i<5;i++){
  const z=-.647+i*.280;
  block(g,.039,.036,.063,x,.052,z,lip,.001);
  plate(g,.056,.064,.009,.005,x+Math.sign(x)*.015,.038,z,metal,0,5);
 }
 for(const x of [-.672,.672]){
  plate(g,.356,.574,.028,.052,x,-.062,.292,black,.005,7);
  for(let i=0;i<8;i++){
   const z=.046+i*.071,xx=x+Math.sign(x)*.178;
   block(g,.083,.010,.022,xx,-.056,z,metal,.001);
   trace(g,[[xx,z],[xx+Math.sign(x)*.057,z],[xx+Math.sign(x)*.074,z+.020]],-.037,.007,traceInk);
   undersideJoint(g,xx+Math.sign(x)*.074,-.037,z+.020,.011,metal,gold);
  }
 }
 const rfPlate=roundedRect(.618,.451,.039);
 rfPlate.holes.push(roundedRect(.559,.398,.029,0,0,new THREE.Path()));
 panel(g,rfPlate,.008,0,-.039,-.493,metal,0,8).rotation.x=Math.PI/2;
 surfaceInk(g,1.447,.754,0,.174,-.075,c=>{
  c.fillStyle='#313f48';c.font='600 44px Segoe UI';c.fillText('WIRELESS MODULE',34,65);
  c.font='25px monospace';c.fillText('M.2 2230  /  RF',35,115);
  c.strokeStyle='#697782';c.strokeRect(35,141,442,1);
  c.font='19px monospace';c.fillText('MAIN  •  AUX',35,184);
  for(let i=0;i<31;i++){const w=i%5===0?4:1.5;c.fillRect(330+i*4,160,w,37+(i%3)*3);}
 });
}
function batteryPart(g,m){
 const {black,metal,gold,pcb}=m,foil=material(0x3e4c59,.39,.59),fold=material(0x252d39,.15,.64),poly=material(0x344454,.11,.55),tape=material(0x4d606c,.06,.76);
 const carrier=roundedRect(3.69,1.97,.119);
 for(const x of [-1.697,1.697])for(const z of [-.78,.78])roundHole(carrier,.057,x,z);
 panel(g,carrier,.078,0,-.016,0,black,.009,11).rotation.x=-Math.PI/2;
 for(const x of [-1.077,0,1.077]){
  plate(g,1.034,1.643,.147,.095,x,.072,.108,fold,.019,12);
  plate(g,1.001,1.572,.143,.089,x,.118,.105,poly,.019,12);
  plate(g,.903,1.430,.093,.005,x,.184,.115,foil,.002,12);
  for(const z of [-.585,.770])plate(g,.896,.049,.014,.005,x,.184,z,fold,.002,6);
  for(const dx of [-.112,.112])plate(g,.138,.133,.016,.011,x+dx,.142,-.710,metal,.002,6);
  for(const [x0,z0,angle] of [[x-.385,-.488,-.22],[x+.382,.689,-.25]]){
   const seam=plate(g,.052,.119,.008,.003,x0,.188,z0,fold,0,5);seam.rotation.y=angle;
  }
 }
 for(const x of [-.530,.530]){
  plate(g,.155,1.217,.047,.008,x,.190,.112,tape,.002,8);
  plate(g,.214,.188,.041,.009,x,.194,.752,tape,.003,8);
  for(let i=0;i<5;i++)block(g,.135,.002,.006,x,.202,.708+i*.022,fold);
 }
 for(const x of [-1.697,1.697])for(const z of [-.78,.78])
  turned(g,[[.046,0],[.077,0],[.074,.008],[.048,.008],[.046,0]],x,.034,z,metal,28);
 plate(g,3.38,.231,.038,.052,0,.054,-.846,pcb,.006,8);
 for(const x of [-1.47,-1.04,-.58,.58,1.0,1.49])smds(g,x,.09,-.859,2,.108,black,metal);
 plate(g,2.98,.24,.047,.015,-.175,.112,-.862,fold,.003,8);
 plate(g,.459,.266,.043,.081,1.159,.096,-.978,black,.010,9);
 plate(g,.338,.081,.018,.064,1.159,.103,-1.116,metal,.007,9);
 for(let i=0;i<6;i++){
  const x=1.019+i*.055;
  block(g,.028,.022,.025,x,.118,-1.15,gold,.002);
  cable(g,[[x,.097,-1.135],[x,.113,-1.20],[x+.021,.126,-1.283],[x+.048,.143,-1.327]],.010,i===0?material(0x6c3c3b,.05,.68):fold,15,8);
 }
 plate(g,.393,.092,.021,.103,1.225,.143,-1.344,black,.006,7);
 plate(g,.190,.037,.013,.025,1.225,.207,-1.340,fold,.003,6);
 for(let i=0;i<6;i++){
  const x=1.099+i*.050;
  block(g,.033,.041,.004,x,.152,-1.394,fold,.001);
  block(g,.018,.014,.003,x,.156,-1.397,gold,.001);
 }
 for(const x of [-1.077,0,1.077]){
  for(const z of [-.719,.822]){
   plate(g,.384,.117,.028,.007,x,-.061,z,tape,.003,8);
   for(let i=0;i<6;i++)block(g,.006,.003,.071,x+(i-2.5)*.045,-.066,z,fold);
  }
  const seam=plate(g,.060,1.141,.025,.007,x+.397,-.060,.092,fold,.002,7);seam.rotation.y=.026;
 }
 for(const x of [.997,1.450]){
  const tab=plate(g,.157,.112,.019,.014,x,.088,-1.138,poly,.002,6);
  tab.rotation.y=(x<1.1 ? .22 : -.22);
 }
 plate(g,.764,.558,.047,.005,0,.191,.205,fold,.002,9);
 surfaceInk(g,.718,.503,0,.197,.203,c=>{
  c.fillStyle='#d6dfe7';c.font='600 54px Segoe UI';c.fillText('Li-ion',24,64);
  c.font='27px Segoe UI';c.fillText('RECHARGEABLE BATTERY PACK',24,107,448);
  c.fillStyle='#a1b5c4';c.fillRect(24,127,458,2);c.font='21px monospace';c.fillText('+ / −   CELL ASSEMBLY',24,165);
  c.font='41px Segoe UI Symbol';c.fillText('♻  ⚠',24,225);
  for(let i=0;i<47;i++)c.fillRect(253+i*4,182,i%3?1.4:2.4,36);
 });
}
function audioPart(g,m){
 const {pcb,metal,black,gold,traceInk,silk}=m;
 const board=new THREE.Shape();
 [[-1.84,-.952],[1.69,-.952],[1.79,-.852],[1.79,.782],[-.371,.782],[-.371,.948],[-.906,.948],[-.906,.850],[-1.003,.850],[-1.003,.948],[-1.242,.948],[-1.242,.782],[-1.84,.782]]
  .forEach(([x,z],i)=>i?board.lineTo(x,-z):board.moveTo(x,-z));board.closePath();
 for(const x of [-1.58,1.58])for(const z of [-.70,.70])roundHole(board,.043,x,z);
 panel(g,board,.060,0,0,0,pcb,.005,8).rotation.x=-Math.PI/2;
 const bracket=roundedRect(2.17,.393,.033);
 for(let i=0;i<5;i++)roundHole(bracket,.093,-.758+i*.379,0);
 const wall=panel(g,bracket,.036,-1.856,.209,0,metal,.0045,14);wall.rotation.y=-Math.PI/2;
 const flange=roundedRect(.166,2.16,.033);roundHole(flange,.038,0,.950);
 panel(g,flange,.036,-1.912,.429,0,metal,.004,8).rotation.x=-Math.PI/2;
 const tab=panel(g,roundedRect(.085,.182,.038),.031,-1.857,.04,1.124,metal,.003,9);tab.rotation.y=-Math.PI/2;
 const jacks=[0x4c7897,0x935c74,0x4f916d,0x515b64,0xa67655].map(c=>material(c,.12,.49));
 for(let i=0;i<5;i++){
  const z=-.758+i*.379;
  plate(g,.239,.246,.041,.166,-1.677,.175,z,black,.009,9);
  const socket=turned(g,[[.057,-.036],[.077,-.036],[.095,-.012],[.105,.009],[.101,.019],[.066,.019],[.058,.010],[.057,-.036]],-1.892,.209,z,jacks[i],36);
  socket.rotation.z=Math.PI/2;
  cylinder(g,.057,.008,-1.829,.209,z,black,28).rotation.z=Math.PI/2;
  for(const dz of [-.058,.058]){
   trace(g,[[-1.564,z+dz],[-1.32,z+dz],[-1.227,z+dz*.75]],.035,.010,traceInk);
   solderVia(g,-1.228,.035,z+dz*.75,.012,metal,black);
  }
 }
 const jacket=material(0x212b37,.21,.46),top=material(0xc3ccd1,.67,.39);
 for(let i=0;i<9;i++){
  const x=.538+(i%3)*.282,z=-.69+Math.floor(i/3)*.41,r=i%3===0 ? .104 : .074,h=i%3===0 ? .245 : .159;
  turned(g,[[0,0],[r*.86,0],[r,.011],[r,h-.021],[r*.93,h-.008],[0,h-.008]],x,.033,z,jacket,28);
  turned(g,[[0,h-.01],[r*.90,h-.01],[r*.87,h+.001],[0,h+.001]],x,.033,z,top,24);
  for(const a of [0,Math.PI/2]){
   const score=block(g,r*1.24,.0013,.009,x,.035+h,z,black);score.rotation.y=a;
  }
  block(g,.017,h*.67,r*.34,x+r*.9,.048+h*.43,z,silk,.0015);
 }
 plate(g,.778,.701,.050,.095,-.391,.091,-.196,black,.009,10);
 for(const side of [-1,1])for(let i=0;i<15;i++){
  const pitch=-.291+i*.0417;
  block(g,.018,.013,.113,-.391+pitch,.043,-.196+side*.378,metal,.002);
  block(g,.113,.013,.018,-.391+side*.409,.043,-.196+pitch,metal,.002);
  trace(g,[[-.391+pitch,-.196+side*.433],[-.391+pitch*.95,-.196+side*.532],[-.34+pitch*.95,-.196+side*.58]],.032,.008,traceInk);
 }
 for(let i=0;i<20;i++){
  const x=-1.214+i*.041;
  if(x>-1.02&&x<-.882)continue;
  for(const face of [-1,1])plate(g,.028,.117,.004,.003,x,face*.036,.881,gold,0,4);
  trace(g,[[x,.824],[x,.760],[x+.063,.698]],.036,.006,traceInk);
 }
 for(let i=0;i<10;i++){
  const z=-.747+i*.150;
  trace(g,[[.171,z],[.322,z],[.38,z+.058],[.493,z+.058]],.036,.008,traceInk);
  solderVia(g,.17,.036,z,.012,gold,black);
 }
 smds(g,-.327,.056,.319,8,.163,black,metal);
 smds(g,-1.18,.054,.61,14,.177,black,metal);
 for(const x of [-1.58,1.58])for(const z of [-.70,.70])
  turned(g,[[.039,0],[.064,0],[.064,.003],[.039,.003],[.039,0]],x,.034,z,gold,20);
 for(let i=0;i<5;i++){
  const z=-.758+i*.379;
  for(const dz of [-.065,.065]){
   undersideJoint(g,-1.686,-.033,z+dz,.035,metal,gold);
   trace(g,[[-1.686,z+dz],[-1.46,z+dz],[-1.38,z+dz*.55],[-1.20,z+dz*.55]],-.033,.009,traceInk);
   undersideJoint(g,-1.20,-.034,z+dz*.55,.016,metal,gold);
  }
 }
 for(let i=0;i<9;i++){
  const x=.538+(i%3)*.282,z=-.69+Math.floor(i/3)*.41;
  for(const dx of [-.036,.036])undersideJoint(g,x+dx,-.033,z,.022,metal,gold);
 }
 for(let row=0;row<3;row++){
  const z=-.535+row*.303;
  for(let i=0;i<9;i++){
   const x=-.806+i*.139;
   block(g,.065,.022,.031,x,-.042,z,black,.003);
   for(const dx of [-.034,.034])block(g,.015,.011,.033,x+dx,-.038,z,metal,.001);
   trace(g,[[x+.037,z],[x+.052,z-.041],[x+.052,z-.107]],-.033,.007,traceInk);
   undersideJoint(g,x+.052,-.034,z-.107,.011,metal,gold);
  }
 }
 plate(g,.353,.198,.019,.047,1.335,.062,.583,black,.003,6);
 for(let i=0;i<10;i++)if(i!==8)block(g,.026,.084,.026,1.210+(i%5)*.061,.120,.544+Math.floor(i/5)*.078,gold,.0015);
 label(g,'F_AUDIO',.335,.092,1.325,.038,.744,'#a6b6ac');
 label(g,'AUDIO CODEC',.689,.16,-.391,.151,-.196,'#b4c1ce');
}
function memoryFallback(g,m){
 const {pcb,metal,black,gold,traceInk}=m;
 outline(g,[[-2,-.535],[2,-.535],[2,.471],[1.93,.471],[1.93,.553],[.392,.553],[.392,.433],[.248,.433],[.248,.553],[-1.93,.553],[-1.93,.471],[-2,.471]],.058,pcb);
 for(const side of [-1,1]){
  for(let i=0;i<8;i++){
   const x=-1.665+i*.475;
   plate(g,.341,.493,.035,.068,x,side*.063,-.099,black,.004,8);
   if(side===1)for(let j=0;j<5;j++){
    const xx=x-.120+j*.059;
    trace(g,[[xx,.169],[xx,.229],[xx-.016,.258],[xx-.016,.349]],.031,.009,traceInk);
    solderVia(g,xx-.016,.032,.347,.008,gold,black);
   }
  }
  for(let i=0;i<71;i++){
   const x=-1.873+i*.0535;if(x>.225&&x<.413)continue;
   plate(g,.036,.148,.006,.003,x,side*.031,.469,gold,0,4);
  }
 }
 smds(g,-1.83,.047,-.438,19,.199,black,metal);
 plate(g,.159,.102,.018,.049,-.02,.055,.284,black,.003,6);
 for(const x of [-1.58,-.62,.31,1.28]){
  for(let j=0;j<4;j++)solderVia(g,x+j*.038,.032,-.368,.010,gold,black);
  label(g,'DRAM',.211,.078,x-.083,.103,-.08,'#a2b0b5');
 }
 label(g,'DIMM',.312,.105,.059,.034,-.344,'#a2bcb3');
}
function storageFallback(g,m){
 const {pcb,metal,black,gold,traceInk,silk}=m;
 const s=new THREE.Shape();
 s.moveTo(-1.95,-.536);s.lineTo(1.95,-.536);s.lineTo(1.95,.263);s.lineTo(1.723,.263);s.lineTo(1.723,.352);
 s.lineTo(1.95,.352);s.lineTo(1.95,.536);s.lineTo(-1.95,.536);s.lineTo(-1.95,.123);
 s.quadraticCurveTo(-1.807,.109,-1.807,0);s.quadraticCurveTo(-1.807,-.109,-1.95,-.123);s.closePath();
 panel(g,s,.066,0,0,0,pcb,.002,12).rotation.x=Math.PI/2;
 for(let i=0;i<24;i++){
  const z=-.496+i*.043;if(z>.238&&z<.377)continue;
  for(const face of [-1,1])plate(g,.217,.028,.006,.003,1.832,face*.034,z,gold,0,4);
  trace(g,[[1.728,z],[1.654,z],[1.556,z*.93]],.035,.006,traceInk);
 }
 for(const [x,w,d] of [[-1.261,.68,.744],[-.461,.63,.731],[.291,.536,.591],[1.093,.594,.686]]){
  plate(g,w,d,.050,.081,x,.077,-.024,black,.005,9);
  for(const z of [-.364,.346])for(let i=0;i<8;i++)solderVia(g,x-w*.43+i*w*.123,.036,z,.008,metal,black);
 }
 const mount=new THREE.Shape();mount.moveTo(0,-.160);mount.absarc(0,0,.160,-Math.PI/2,Math.PI/2,false);
 mount.lineTo(0,.121);mount.absarc(0,0,.121,Math.PI/2,-Math.PI/2,true);mount.closePath();
 panel(g,mount,.002,-1.949,.035,0,gold,0,14).rotation.x=-Math.PI/2;
 for(const z of [-.455,.431])smds(g,-1.59,.051,z,16,.209,black,metal);
 block(g,.31,.003,.017,.96,.036,.383,silk);
 label(g,'NVMe',.570,.158,1.092,.124,-.018,'#a4b6c3');
 for(const [x,s] of [[-1.261,'NAND'],[-.461,'NAND'],[.291,'CACHE']])label(g,s,.346,.090,x,.125,-.021,'#8797a2');
 for(let i=0;i<7;i++)trace(g,[[.47+i*.055,.385],[.515+i*.052,.410],[.82+i*.049,.410]],.035,.008,traceInk);
}
function gpuFallback(g,m){
 const {pcb,metal,black,gold,blue}=m,back=material(0x343f4e,.44,.46),accent=material(0x56718a,.55,.36);
 plate(g,3.02,1.52,.069,.061,-.45,.026,0,pcb,.006,10);
 const rear=roundedRect(3.90,1.51,.092);
 for(let i=0;i<8;i++)rear.holes.push(roundedRect(.035,.399,.016,1.209+i*.079,0,new THREE.Path()));
 panel(g,rear,.044,0,-.032,0,back,0,5).rotation.x=-Math.PI/2;
 for(let i=0;i<47;i++)block(g,.026,.218,1.392,-1.80+i*.078,.167,0,metal);
 const deck=roundedRect(3.872,1.553,.106);
 for(const x of [-.963,.963])roundHole(deck,.585,x,0);
 panel(g,deck,.052,0,.280,0,black,.008,15).rotation.x=-Math.PI/2;
 for(const z of [-.711,.711])plate(g,3.75,.081,.033,.187,0,.213,z,back,.008,10);
 const bracket=roundedRect(1.78,.563,.036);
 for(let i=0;i<4;i++)bracket.holes.push(roundedRect(.266,.107,.018,-.52+i*.35,.087,new THREE.Path()));
 for(let i=0;i<10;i++)bracket.holes.push(roundedRect(.055,.101,.024,-.648+i*.141,-.123,new THREE.Path()));
 panel(g,bracket,.049,-2.008,.178,0,metal,.002,7).rotation.y=-Math.PI/2;
 for(let i=0;i<4;i++){
  block(g,.03,.09,.24,-1.967,.265,-.52+i*.35,black,.004);
  block(g,.035,.014,.151,-1.991,.258,-.52+i*.35,blue,.002);
 }
 for(let i=0;i<27;i++)plate(g,.053,.154,.006,.004,-1.414+i*.078,.056,.803,gold);
 const fan=impeller(.527,.030,9);
 for(const x of [-.963,.963]){
  turned(g,[[.552,0],[.592,0],[.611,.022],[.608,.042],[.556,.043],[.552,0]],x,.313,0,accent,56);
  const rotor=new THREE.Group();rotor.position.set(x,.363,0);rotor.rotation.x=-Math.PI/2;g.add(rotor);
  rotor.userData.focusSpin=-8;
  rotor.add(new THREE.Mesh(fan.clone(),back));
  turned(g,[[0,0],[.125,0],[.143,.015],[.143,.058],[.125,.069],[0,.069]],x,.347,0,black,42);
  cylinder(g,.087,.006,x,.419,0,accent,34);
 }
 for(const x of [-1.76,1.76])for(const z of [-.640,.640])fastener(g,x,.311,z,.035,metal,black);
 for(const z of [-.718,.718])for(let i=0;i<11;i++)block(g,.015,.032,.005,-1.68+i*.087,.264,z,metal);
 plate(g,.395,.192,.026,.078,1.11,.128,-.773,back,.004,7);
 for(let i=0;i<4;i++)for(let j=0;j<2;j++)block(g,.037,.031,.004,.973+i*.087,.108+j*.051,-.873,gold);
 fan.dispose();
}
function boardFallback(g,m){
 const {pcb,metal,black,gold,blue,traceInk}=m;
 const base=roundedRect(3.47,3.42,.061);
 for(const x of [-1.56,1.56])for(const z of [-1.53,1.53])roundHole(base,.049,x,z);
 panel(g,base,.070,0,0,0,pcb,.003,9).rotation.x=-Math.PI/2;
 plate(g,1.001,1.063,.091,.067,-.411,.074,-.601,black,.009,10);
 const frame=roundedRect(.941,.992,.052);
 frame.holes.push(roundedRect(.66,.72,.024,0,0,new THREE.Path()));
 panel(g,frame,.045,-.411,.124,-.601,metal,.004,10).rotation.x=-Math.PI/2;
 for(let i=0;i<14;i++)for(let j=0;j<15;j++)
  block(g,.027,.002,.029,-.690+i*.0427,.110,-.89+j*.041,gold);
 for(let i=0;i<4;i++){
  const x=.43+i*.25;
  plate(g,.141,1.89,.038,.092,x,.084,-.508,black,.004,7);
  block(g,.045,.003,1.60,x,.132,-.508,traceInk);
  for(const z of [-1.411,.385])plate(g,.132,.110,.020,.060,x,.134,z,i%2?blue:metal,.004,7);
 }
 for(let i=0;i<3;i++){
  const z=.539+i*.458;
  plate(g,2.28,.144,.020,.083,-.327,.082,z,black,.004,6);
  block(g,2.043,.003,.038,-.359,.125,z,gold);
  block(g,.088,.028,.133,.837,.119,z,metal,.004);
 }
 for(const z of [-1.246,-.809,-.366]){
  plate(g,.383,.352,.044,.258,-1.535,.174,z,metal,.008,8);
  block(g,.013,.120,.228,-1.738,.191,z,black,.002);
 }
 plate(g,1.403,.304,.042,.133,-.656,.104,-1.368,metal,.009,9);
 for(let i=0;i<25;i++)block(g,.021,.070,.277,-1.308+i*.054,.186,-1.368,metal);
 plate(g,.621,.601,.066,.133,1.047,.105,.998,blue,.009,10);
 for(let i=0;i<11;i++)block(g,.022,.056,.541,.791+i*.051,.182,.998,metal,.002);
 smds(g,-1.34,.057,1.521,15,.178,black,metal);
 for(const x of [-1.56,1.56])for(const z of [-1.53,1.53])
  turned(g,[[.043,0],[.069,0],[.069,.004],[.043,.004],[.043,0]],x,.035,z,gold,24);
 for(let i=0;i<15;i++){
  const z=-.581+i*.047;
  trace(g,[[.064,z],[.224,z],[.293,z+.059],[.433,z+.059]],.037,.008,traceInk);
  solderVia(g,.219,.038,z,.013,gold,black);
 }
 cylinder(g,.166,.042,.673,.059,.344,metal,36);
 cylinder(g,.131,.003,.673,.082,.344,black,30);
 label(g,'+',.120,.095,.673,.085,.344,'#bec7c9');
}
function original(kind,brand,screenTexture){
 const g=new THREE.Group(),m={pcb:material(0x214440,.18,.62),metal:material(0xa7b5c2,.66,.34),
  black:material(0x18202a,.13,.62),gold:material(0xc3a364,.60,.39),blue:material(0x365d80,.50,.37),
  traceInk:material(0x3f6359,.30,.56),silk:material(0x94a6ab,.02,.74)};
 if(kind==='显示器'||kind==='系统')monitorPart(g,screenTexture,m);
 else if(kind==='BIOS')flashPart(g,m);
 else if(kind==='NPU')npuPart(g,m);
 else if(kind==='处理器')cpuPart(g,brand,m);
 else if(kind==='内存')memoryFallback(g,m);
 else if(kind==='硬盘')storageFallback(g,m);
 else if(kind==='电池')batteryPart(g,m);
 else if(kind==='显卡')gpuFallback(g,m);
 else if(kind==='网卡')wirelessPart(g,m);
 else if(kind==='声卡')audioPart(g,m);
 else boardFallback(g,m);
 batchStatic(g);return g;
}
function normalize(root){const bounds=new THREE.Box3().setFromObject(root);const center=bounds.getCenter(new THREE.Vector3());const size=bounds.getSize(new THREE.Vector3());root.position.sub(center);const wrapper=new THREE.Group();wrapper.add(root);wrapper.scale.setScalar(4.1/Math.max(size.x,size.y,size.z));const result=new THREE.Group();result.add(wrapper);return result;}
function addDimmContacts(result){
 // The supplied DIMM has the connector shape but no plated-finger detail.
 // Project the contacts onto that asset's PCB, leaving its keying notch clear.
 let board;
 result.traverse(o=>{if(o.isMesh&&o.name.includes('Cardea_Diff'))board=o;});
 if(!board)return;
 result.updateMatrixWorld(true);
 const bounds=new THREE.Box3().setFromObject(board),ray=new THREE.Raycaster(),z=bounds.max.z-.044;
 const contact=material(0xc9a65b,.56,.4);
 for(let i=0;i<62;i++)for(const face of [-1,1]){
  const x=THREE.MathUtils.lerp(bounds.min.x+.11,bounds.max.x-.11,i/61);
  ray.ray.origin.set(x,face,z);ray.ray.direction.set(0,-face,0);
  const hit=ray.intersectObject(board,false)[0];
  if(hit)block(result,.038,.004,.068,x,hit.point.y+face*.003,z,contact);
 }
 batchStatic(result);
}
export async function hardwareComponent(kind,brand,screenTexture){
 const key=`${kind}:${brand||''}:${['显示器','系统'].includes(kind)?screenTexture?.uuid||'default':''}`;
 if(!cache.has(key))cache.set(key,(async()=>{
  const file=kind==='处理器'?(brand==='intel'?'cpu':null):({'内存':'ram','显卡':'gpu','硬盘':'storage','主板':'motherboard'})[kind];
  let root,source='procedural';
  if(file){try{
   root=(await loader.loadAsync(`models/${file}.glb`)).scene;
   if(kind==='显卡'){
    root.rotateZ(Math.PI/2);
    // Both pivot nodes in this GLB have a local Z axis through the hub.
    for(const [name,rate] of [['fan',-8],['fan_2',8]]){
     const pivot=root.getObjectByName(name);
     if(pivot)pivot.userData.focusSpin=rate;
    }
   }
   if(kind==='内存'){
    root.rotateX(Math.PI/2);root.rotateY(Math.PI/2);
    // Present the keyed connector edge toward the viewer, with the light bar behind it.
    root.rotateOnWorldAxis(new THREE.Vector3(0,1,0),Math.PI);
   }
   let hasTexture=false;
   const materials=new Set();
   root.traverse(node=>{if(node.isMesh)for(const m of Array.isArray(node.material)?node.material:[node.material])materials.add(m);});
   const minRoughness={cpu:.38,gpu:.43,ram:.38,storage:.54,motherboard:.38}[file];
   for(const m of materials){
    if(m.map){hasTexture=true;m.map.anisotropy=4;}
    m.roughness=Math.max(minRoughness,m.roughness);
    m.envMapIntensity=file==='storage' ? .45 : .68;
    if(m.emissive&&!m.emissiveMap)m.emissive.setHex(0);
    if(file==='ram'&&m.name==='Case'){
     m.color.setHex(0x343e4b);m.metalness=.25;m.roughness=.62;
    }
    if(file==='ram'&&m.name==='Cardea_Diff')m.color.setHex(0x32534c);
    if(file==='ram'&&m.name==='ASUS_Emissive_WhiteMaterial'){
     m.color.setHex(0xb5d4e4);m.emissive.setHex(0x33586f);m.emissiveIntensity=.28;
    }
   }
   // The bundled DIMM has material colors, not image textures; it is valid without a map.
   if(['cpu','gpu','storage'].includes(file)&&!hasTexture)throw new Error('No component textures loaded: '+file);
   source=file;
  }catch(error){console.warn('Hardware component fallback',file,error);root=original(kind,brand,screenTexture);}}
  else root=original(kind,brand,screenTexture);
  const result=normalize(root);
  if(source==='ram')addDimmContacts(result);
  if(source==='cpu'&&brand==='intel'){
   const bounds=new THREE.Box3().setFromObject(result);
   label(result,'intel',.87,.27,0,bounds.max.y+.012,-.12,'#314253');
  }
  result.userData={source,brand:brand||'generic'};return result;
 })());
 return (await cache.get(key)).clone(true);
}
