import * as THREE from 'three';
import { batchStatic, box as solidBox, panel as solidPanel, roundedRect, roundHole, cylinder, turned, cable, impeller } from './geometry.js';

// Tower axes: -X is the glass side, +Z the front. The motherboard faces the glass.
const SIDE=-Math.PI/2;
const PCB_X=.375, BOARD_W=1.214, BOARD_H=1.57, BOARD_Y=1.402, BOARD_Z=-.018;
function box(g,w,h,d,x,y,z,mat,r=0){
 // Tiny stamped parts have flat faces; only the larger housings need a rounded edge.
 return solidBox(g,w,h,d,x,y,z,mat,Math.min(w,h,d)<.045?0:r);
}
function panel(g,shape,thickness,x,y,z,mat,bevel=0,segments=6){
 // A stamped or laser-cut opening has a straight wall, even when the outer silhouette is rounded.
 return solidPanel(g,shape,thickness,x,y,z,mat,shape.holes.length>=3?0:bevel,segments,1);
}
function plate(g,w,d,r,thickness,x,y,z,mat,bevel=0,segments=6){
 return panel(g,roundedRect(w,d,r),thickness,x,y,z,mat,bevel,segments).rotateX(-Math.PI/2);
}
function sidePanel(g,shape,thickness,x,y,z,mat,bevel=0,segments=6){
 const o=panel(g,shape,thickness,x,y,z,mat,bevel,segments);
 o.rotation.y=SIDE;return o;
}
function sideDisk(g,r,h,x,y,z,mat,segments=20){
 const o=cylinder(g,r,h,x,y,z,mat,segments);o.rotation.z=Math.PI/2;return o;
}
function screw(g,x,y,z,r,metal,dark,axis='side'){
 if(axis==='side'){
  sideDisk(g,r,.006,x,y,z,metal,12);
  box(g,.0015,r*.27,r*1.22,x-.0036,y,z,dark).rotation.x=.48;
 }else{
  cylinder(g,r,.006,x,y,z,metal,12);
  box(g,r*1.22,.0015,r*.27,x,y+.0036,z,dark).rotation.y=.48;
 }
}
function slot(shape,w,h,r,x,y){
 shape.holes.push(roundedRect(w,h,r,x,y,new THREE.Path()));
}
function cap(g,x,y,z,r,h,m){
 sideDisk(g,r,h,x-h/2,y,z,m.edge,10);
 sideDisk(g,r*.86,.003,x-h-.001,y,z,m.tin,10);
 const score=box(g,.0015,r*.23,r*1.08,x-h-.0033,y,z,m.dark);score.rotation.x=.55;
}
function chip(g,x,y,z,h,w,m){
 box(g,.014,h,w,x-.011,y,z,m.dark,.004);
 for(const s of [-1,1])for(let i=0;i<7;i++){
  const z0=z+(i-3)*w*.116;
  box(g,.002,.013,w*.052,x-.001,y+s*(h/2+.003),z0,m.tin);
 }
}
function rotatingFan(g,m,{id,x,y,z,axis,radius,depth,count,rate,blade=impeller(radius,depth,count),color=m.fan,trails=false}){
 const mount=new THREE.Group();mount.position.set(x,y,z);g.add(mount);
 if(axis==='side')mount.rotation.y=SIDE;
 if(axis==='down')mount.rotation.x=Math.PI/2;
 if(axis==='up')mount.rotation.x=-Math.PI/2;
 const rotor=new THREE.Group();mount.add(rotor);m.rotors.push({id,rotor,rate});
 rotor.add(new THREE.Mesh(blade,color));
 cylinder(rotor,radius*.263,depth*1.55,0,0,.004,m.edge,26).rotation.x=Math.PI/2;
 cylinder(rotor,radius*.161,.005,0,0,.005+depth*.78,m.trim,22).rotation.x=Math.PI/2;
 if(trails){
  const material=new THREE.MeshBasicMaterial({color:0xd8e4ed,transparent:true,opacity:0,depthWrite:false});
  m.trails.push(material);
  for(const lag of [.12,.24]){
   const ghost=new THREE.Mesh(blade.clone(),material);ghost.rotation.z=lag;ghost.position.z=.003;rotor.add(ghost);
  }
 }
 batchStatic(rotor);return mount;
}
function heatSink(g,x,y,z,h,w,m){
 sidePanel(g,roundedRect(w,h,.021),.039,x,y,z,m.radiator,.003,5);
 const n=Math.floor(w/.023);
 for(let i=0;i<n;i++){
  const local=(i-(n-1)/2)*.023,shorten=Math.abs(local)/w;
  box(g,.017,h*(.83-.25*shorten),.009,x-.025,y+.007,z+local,i%5===0?m.tin:m.trim,.0025);
 }
 for(const dz of [-w*.34,w*.34])screw(g,x-.023,y-h*.36,z+dz,.010,m.tin,m.dark);
}

function boardArtwork(){
 const cv=document.createElement('canvas');cv.width=768;cv.height=1024;
 const c=cv.getContext('2d'),W=cv.width,H=cv.height;
 const point=(z,y)=>[(z-BOARD_Z+BOARD_W/2)/BOARD_W*W,(BOARD_Y+BOARD_H/2-y)/BOARD_H*H];
 let seed=0x3ae03;
 const random=()=>((seed=(Math.imul(1664525,seed)+1013904223)>>>0)/4294967296);
 const base=c.createLinearGradient(0,0,W,H);
 base.addColorStop(0,'#252f36');base.addColorStop(.53,'#243132');base.addColorStop(1,'#263943');
 c.fillStyle=base;c.fillRect(0,0,W,H);
 for(let i=0;i<6400;i++){
  c.fillStyle=i%2?'rgba(151,167,163,.08)':'rgba(6,15,19,.12)';
  c.fillRect(random()*W,random()*H,1+random(),1+random());
 }
 const trace=(coords,color,w=1.5)=>{
  c.beginPath();coords.forEach(([z,y],i)=>{const [x,v]=point(z,y);if(i)c.lineTo(x,v);else c.moveTo(x,v);});
  c.strokeStyle=color;c.lineWidth=w;c.stroke();
 };
 const via=(z,y,r=2.8)=>{
  const [x,v]=point(z,y);
  c.fillStyle='#82785b';c.beginPath();c.arc(x,v,r,0,Math.PI*2);c.fill();
  c.fillStyle='#222b30';c.beginPath();c.arc(x,v,r*.48,0,Math.PI*2);c.fill();
 };
 const pad=(z,y,w,h,color)=>{
  const [x,v]=point(z,y);c.fillStyle=color;c.fillRect(x-w/2,v-h/2,w,h);
 };
 // Parallel buses fan out between the socket, memory, PCIe slots and chipset.
 for(let i=0;i<32;i++){
  const y=1.452+i*.016,z=-.101+i*.0018,shift=(i%8)*.009;
  trace([[z,y],[z+.081+shift,y],[z+.119+shift,y-.038],[.301+(i%4)*.045,y-.038]],i%4?'#405052':'#566365',1.25);
  via(.301+(i%4)*.045,y-.038,1.8);
 }
 for(let i=0;i<36;i++){
  const z=-.563+i*.0258,y=1.010+(i%8)*.023;
  trace([[z,y],[z+.049,y-.049],[z+.049,.905+(i%4)*.011],[z+.093,.861+(i%4)*.011]],i%6?'#39494c':'#526365',1.3);
  via(z+.093,.861+(i%4)*.011,2);
 }
 for(let i=0;i<18;i++){
  const y=.745+i*.076;
  trace([[.552,y],[.499,y],[.430,y+.070],[.328-(i%4)*.05,y+.070]],'#4d5b5b',1.4);
  via(.328-(i%4)*.05,y+.070,2.5);
 }
 for(let i=0;i<255;i++){
  const z=-.592+random()*1.146,y=.643+random()*1.498;
  if(i%4===0){
   pad(z,y,5,2.4,'#7d806e');pad(z+.012,y,5,2.4,'#7d806e');pad(z+.006,y,4,2.4,'#1d252a');
  }else via(z,y,1.2+random()*1.1);
 }
 const ink='#818e8c';
 for(const [z,y,w,h] of [[-.225,1.732,.428,.455],[.257,1.746,.336,.638],[-.238,1.122,.602,.092],[-.087,.904,.550,.085],
   [.389,.917,.199,.259],[.225,1.342,.500,.089],[-.507,.745,.147,.107],[.463,.700,.150,.071]]){
  const [x,v]=point(z-w/2,y+h/2);c.strokeStyle='#617374';c.lineWidth=1.4;c.strokeRect(x,v,w/BOARD_W*W,h/BOARD_H*H);
 }
 c.font='11px monospace';c.fillStyle=ink;
 for(const [z,y,label] of [[-.35,1.445,'CPU SOCKET'],[.098,2.084,'DIMM A1   A2   B1   B2'],[-.542,1.271,'PCIe ×16'],[.130,1.285,'M.2  2280'],[-.424,.808,'PCIe ×4'],
  [.354,.760,'PCH'],[.318,.679,'F_PANEL'],[-.590,.683,'HD AUDIO'],[.379,1.511,'ATX 24P']]){
  const [x,v]=point(z,y);c.fillText(label,x,v);
 }
 const map=new THREE.CanvasTexture(cv);map.colorSpace=THREE.SRGBColorSpace;map.anisotropy=4;
 const paint=new THREE.MeshStandardMaterial({map,roughness:.70,metalness:.12});
 paint.userData.ownsMap=true;return paint;
}

function motherboard(g,m){
 const outline=roundedRect(BOARD_W,BOARD_H,.025);
 sidePanel(g,outline,.023,PCB_X+.011,BOARD_Y,BOARD_Z,m.pcbEdge,.002,7);
 const mask=new THREE.ShapeGeometry(outline,7),p=mask.attributes.position,uv=mask.attributes.uv;
 for(let i=0;i<uv.count;i++)uv.setXY(i,p.getX(i)/BOARD_W+.5,p.getY(i)/BOARD_H+.5);
 const paint=new THREE.Mesh(mask,boardArtwork());
 paint.position.set(PCB_X-.0045,BOARD_Y,BOARD_Z);paint.rotation.y=SIDE;g.add(paint);
 // Standoff screws, power stages, memory slots, shielded I/O and peripheral headers.
 for(const y of [.667,1.43,2.137])for(const z of [-.564,.540])
  screw(g,PCB_X-.007,y,z,.018,m.tin,m.dark);
 heatSink(g,.336,2.045,-.305,.147,.528,m);
 heatSink(g,.341,1.734,-.541,.438,.107,m);
 for(let i=0;i<8;i++){
  const z=-.502+i*.061;
  box(g,.032,.036,.041,.352,1.935,z,m.edge,.004);
  cap(g,.364,1.880,z,.015,.027,m);
 }
 for(let i=0;i<6;i++){
  const y=1.530+i*.065;
  box(g,.027,.038,.038,.352,y,-.455,m.edge,.003);
  cap(g,.364,y,-.404,.012,.024,m);
 }
 for(let i=0;i<4;i++){
  const z=.112+i*.083;
  box(g,.067,.644,.040,.346,1.744,z,i%2?m.edge:m.dark,.006);
  box(g,.003,.554,.010,.310,1.745,z,m.pcbEdge,.001);
  for(const y of [1.415,2.071]){
   box(g,.077,.046,.046,.347,y,z,m.radiator,.005);
   box(g,.012,.016,.030,.303,y+.010,z,m.trim,.003);
  }
  if(i===1||i===3){
   box(g,.126,.578,.030,.271,1.747,z,m.pcbEdge,.003);
   const outline=roundedRect(.050,.551,.016);
   sidePanel(g,outline,.094,.263,1.745,z,m.radiator,.0035,8);
   for(let j=0;j<8;j++)box(g,.008,.038,.052,.213,1.509+j*.064,z,m.trim,.0028);
   sidePanel(g,roundedRect(.025,.522,.012),.008,.205,1.746,z,m.diffuser,.0017,8);
  }
 }
 for(const [y,z,len] of [[1.122,-.224,.633],[.895,-.271,.504],[.780,-.414,.202]]){
  box(g,.064,.046,len,.343,y,z,m.dark,.004);
  box(g,.003,.013,len*.88,.309,y,z,m.pcbEdge,.001);
  for(const dz of [-len*.425,len*.425])box(g,.020,.050,.022,.316,y,z+dz,m.radiator,.003);
 }
 heatSink(g,.343,1.331,.154,.082,.556,m);
 heatSink(g,.334,.952,.381,.205,.267,m);
 const batt=turned(g,[[0,0],[.056,0],[.065,.005],[.065,.014],[.060,.019],[0,.019]],.347,.767,.203,m.tin,28);
 batt.rotation.z=Math.PI/2;
 sideDisk(g,.072,.015,.358,.767,.203,m.edge,24);
 box(g,.008,.014,.054,.325,.811,.205,m.tin,.003);
 for(const [y,z] of [[.737,-.338],[.982,-.492],[1.487,.512],[1.254,-.455],[.806,.388]])chip(g,.368,y,z,.077,.079,m);
 for(let i=0;i<8;i++)cap(g,.367,.692+i*.064,-.531+i%2*.046,.013,.027,m);
 for(let i=0;i<9;i++){
  const z=-.428+i*.113;
  box(g,.024,.037,.047,.358,.639,z,m.edge,.003);
  for(let j=0;j<3;j++)box(g,.013,.003,.0035,.342,.638+(j-1)*.009,z+.013,m.gold);
 }
 for(let i=0;i<4;i++){
  box(g,.061,.044,.060,.343,.710+i*.052,.557,m.edge,.006);
  box(g,.003,.021,.042,.310,.710+i*.052,.557,m.dark,.003);
 }
}
function cpuCooling(g,m){
 const y=1.731,z=-.190;
 sidePanel(g,roundedRect(.406,.399,.030),.030,.350,y,z,m.tin,.003,8);
 sidePanel(g,roundedRect(.317,.309,.027),.055,.311,y,z,m.radiator,.006,8);
 for(const dy of [-.211,.211])for(const dz of [-.189,.189]){
  box(g,.065,.040,.043,.330,y+dy,z+dz,m.edge,.005);
  screw(g,.294,y+dy,z+dz,.014,m.tin,m.dark);
 }
 for(let i=0;i<19;i++){
  const dy=(i-9)*.0192,w=.386-.019*(Math.abs(i-9)/9);
  plate(g,.221,w,.028,.009,.223,y+dy,z,m.fin,0,2);
 }
 for(let i=0;i<4;i++){
  const dz=.119+i*.021,side=i<2?-1:1,k=i%2,x=.156+k*.033;
  cable(g,[[.307,y-.091,z+side*(dz-k*.042)],[.286,y-.161,z+side*(dz-k*.042)],
   [.225,y-.199,z+side*(dz-k*.034)],[x,y-.179,z+side*(dz-k*.041)],[x,y+.179,z+side*(dz-k*.041)]],.0108,m.nickel,18,7);
  cylinder(g,.014,.016,x,y+.184,z+side*(dz-k*.041),m.nickel,16);
 }
 const frame=roundedRect(.424,.421,.043);
 roundHole(frame,.184);
 sidePanel(g,frame,.044,.105,y,z,m.radiator,.004,10);
 rotatingFan(g,m,{id:'cpu',x:.086,y,z,axis:'side',radius:.175,depth:.020,count:9,rate:1.23});
 for(const dy of [-.166,.166])for(const dz of [-.169,.169])
  screw(g,.079,y+dy,z+dz,.015,m.tin,m.dark);
 for(const dy of [-.137,.137]){
  cable(g,[[.060,y+dy,z-.198],[.093,y+dy,z-.228],[.217,y+dy,z-.228],[.240,y+dy*.83,z-.207]],.005,m.tin,16,6);
 }
 cable(g,[[.106,y+.190,z-.183],[.139,y+.233,z-.227],[.223,y+.273,z-.202],[.338,y+.268,z-.160]],.008,m.edge,20,6);
}
function graphicsCard(g,m){
 const y=1.111,z=-.075,cx=-.046;
 // PCIe bracket, card laminate, cooler stack, die-cast shroud and perforated backplate.
 box(g,.451,.170,.061,-.132,y,-.724,m.edge,.003);
 const outputs=roundedRect(.471,.196,.017);
 const ports=[[-.158,.061],[-.052,.074],[.057,.074],[.162,.067]];
 for(const [x,w] of ports)slot(outputs,w,.042,.006,x,.039);
 for(let i=0;i<9;i++)slot(outputs,.027,.022,.005,-.172+i*.043,-.044);
 panel(g,outputs,.015,-.132,y,-.792,m.tin,0,3).rotation.y=Math.PI;
 for(const [x,w] of ports){
  box(g,w*.86,.031,.010,-.132-x,y+.039,-.784,m.dark,.0015);
  box(g,w*.70,.006,.011,-.132-x,y+.036,-.793,m.trim,.001);
 }
 plate(g,.637,.950,.050,.027,cx,y+.088,z-.100,m.pcbEdge,.0025,6);
 for(let i=0;i<33;i++){
  const zz=z-.53+i*.031;
  box(g,.527,.091,.018,cx,y+.026,zz,m.fin);
  if(i%4===0)box(g,.535,.008,.010,cx,y+.074,zz,m.tin);
 }
 for(const off of [-.211,.198])
  cable(g,[[cx+off,y-.012,z-.484],[cx+off,y+.002,z-.376],
    [cx+off*.79,y+.014,z+.298],[cx+off*.89,y+.025,z+.414],[cx+off*.68,y+.061,z+.467]],.010,m.nickel,22,8);
 const back=roundedRect(.642,1.165,.051);
 for(let i=0;i<9;i++)slot(back,.026,.194,.009,-.195+i*.047,-.348);
 const cross=[[-.081,-.071,.047,.016],[.081,-.071,.047,.016],[-.081,.071,.047,.016],[.081,.071,.047,.016]];
 for(const [xx,zz,w,h] of cross)slot(back,w,h,.007,xx,zz);
 panel(g,back,.019,cx,y+.119,z,m.radiator,.004,7).rotation.x=-Math.PI/2;
 // Folded stiffeners frame the screw pattern without blocking the flow-through slots.
 for(const x of [-.205,.212]){
  plate(g,.028,.604,.012,.006,cx+x,y+.133,z-.133,m.trim,.001,5);
  plate(g,.056,.118,.015,.005,cx+x*.72,y+.132,z+.182,m.edge,.001,5).rotation.y=.46;
 }
 for(const xx of [-.261,.269])for(const zz of [-.507,.055,.504])
  screw(g,cx+xx,y+.134,z+zz,.011,m.tin,m.dark,'top');
 // Spring screw seats secure the backplate behind the GPU die.
 for(const dx of [-.123,.123])for(const dz of [-.111,.111]){
  const xx=cx+dx,zz=z+dz-.121;
  turned(g,[[.011,0],[.026,0],[.026,.002],[.024,.005],[.011,.005],[.011,0]],xx,y+.135,zz,m.stamped,24);
  screw(g,xx,y+.139,zz,.013,m.tin,m.dark,'top');
 }
 for(const side of [-1,1]){
  const shape=new THREE.Shape();
  [[.150,.450],[.223,.392],[.236,.301],[.215,.280],[.193,.368],[.127,.424]].forEach(([x,z],i)=>i?shape.lineTo(side*x,z):shape.moveTo(side*x,z));
  shape.closePath();
  panel(g,shape,.003,cx,y+.132,z,m.stamped,.001,4).rotateX(-Math.PI/2);
 }
 // Connector on the PCB edge, visible through the lower part of the shroud.
 for(let i=0;i<13;i++){
  const zz=z-.478+i*.025;
  box(g,.041,.005,.015,.259,y+.082,zz,m.tin);
  box(g,.020,.007,.016,.275,y+.079,zz,m.gold);
 }
 for(const dz of [-.523,.522]){
  const outer=roundedRect(.608,.134,.035);
  panel(g,outer,.068,cx,y+.003,z+dz,m.edge,.005,8);
 }
 const fanDeck=roundedRect(.632,1.162,.042);
 for(const dz of [-.347,0,.347])roundHole(fanDeck,.158,0,dz);
 panel(g,fanDeck,.029,cx,y-.033,z,m.edge,.003,8).rotation.x=-Math.PI/2;
 const bladeGeo=impeller(.144,.012,7);
 for(const [i,dz] of [-.347,0,.347].entries()){
  const blade=bladeGeo.clone(),middle=i===1;
  // Opposing centre fan uses opposite pitch as well as opposite rotation.
  if(middle)blade.rotateY(Math.PI);
  rotatingFan(g,m,{id:`gpu${i+1}`,x:cx,y:y-.052,z:z+dz,axis:'down',radius:.144,depth:.012,
   blade,rate:middle?-1.10:1.10});
 }
 bladeGeo.dispose();
 // Long side openings expose the fin edges and copper-coloured heatpipes.
 const cover=roundedRect(1.163,.150,.042);
 for(const dx of [-.341,0,.342])slot(cover,.263,.048,.018,dx,-.004);
 sidePanel(g,cover,.026,-.367,y+.034,z,m.edge,.004,8);
 sidePanel(g,roundedRect(.524,.010,.005),.004,-.384,y+.092,z-.172,m.diffuser,.001,5);
 for(const dz of [-.496,.479])screw(g,-.387,y+.067,z+dz,.010,m.tin,m.dark);
 for(let i=0;i<6;i++)box(g,.029,.006,.044,.253,y+.091,z-.349+i*.057,m.gold);
}
function rearHardware(g,m){
 const cy=1.837,fx=-.130;
 const frame=roundedRect(.641,.609,.037);
 roundHole(frame,.280);
 for(const dx of [-.276,.276])for(const dy of [-.260,.260])roundHole(frame,.014,dx,dy);
 panel(g,frame,.053,fx,cy,-.711,m.trim,.002,16);
 const ring=new THREE.Mesh(new THREE.TorusGeometry(.291,.011,10,64),m.accent);
 ring.position.set(fx,cy,-.674);g.add(ring);
 rotatingFan(g,m,{id:'rear',x:fx,y:cy,z:-.669,radius:.274,depth:.022,count:7,rate:1,color:m.caseBlade,trails:true});
 // Open wire guard and four slender motor supports on the exhaust side.
 for(const radius of [.075,.130,.185,.238,.292]){
  const hoop=new THREE.Mesh(new THREE.TorusGeometry(radius,.0035,5,44),m.trim);
  hoop.position.set(fx,cy,-.793);g.add(hoop);
 }
 for(const a of [Math.PI/4,Math.PI*.75])
  box(g,.573,.010,.008,fx,cy,-.797,m.edge,.001).rotation.z=a;
 for(const dx of [-.276,.276])for(const dy of [-.260,.260]){
  const screwHead=cylinder(g,.013,.011,fx+dx,cy+dy,-.681,m.tin,16);
  screwHead.rotation.x=Math.PI/2;
 }
 cable(g,[[fx+.29,cy-.254,-.705],[fx+.251,cy-.360,-.675],[.276,cy-.398,-.623],[.356,cy-.394,-.556]],.006,m.cableJacket,18,6);
 panel(g,roundedRect(.159,.807,.023),.050,.357,1.698,-.723,m.radiator,.004,8);
 for(const [yy,h,w] of [[2.032,.090,.114],[1.895,.103,.112],[1.755,.126,.112],[1.595,.100,.103],[1.452,.106,.111]]){
  const s=roundedRect(w,h,.011);
  slot(s,w-.018,h-.020,.007,0,0);
  panel(g,s,.028,.359,yy,-.799,m.tin,0,4).rotation.y=Math.PI;
  panel(g,roundedRect(w-.022,h-.022,.005),.009,.359,yy,-.782,m.dark,0,3).rotation.y=Math.PI;
  box(g,w*.68,.011,.012,.359,yy-.012,-.794,yy===1.755?m.trim:m.blue,.001);
 }
 for(let i=0;i<6;i++){
  const yy=.663+i*.060;
  const slotCover=roundedRect(.451,.045,.008);
  for(let j=0;j<5;j++)slot(slotCover,.062,.013,.003,-.160+j*.080,0);
  panel(g,slotCover,.011,-.132,yy,-.796,m.radiator,0,2);
 }
 // The expansion brackets are held by a raised rail, which continues below the GPU bracket.
 const keeper=roundedRect(.065,.440,.016);
 for(let i=0;i<6;i++)roundHole(keeper,.010,0,-.184+i*.060);
 panel(g,keeper,.013,-.373,.847,-.760,m.stamped,0,6);
 for(let i=0;i<6;i++){
  const y=.663+i*.060;
  cylinder(g,.013,.009,-.374,y,-.751,m.tin,16).rotateX(Math.PI/2);
  box(g,.016,.002,.001,-.374,y,-.745,m.dark).rotation.z=.5;
 }
}
function shroud(g,m){
 // Power supply and drive sled are visible through the open side of the shroud.
 plate(g,.646,.584,.030,.023,-.038,.118,-.354,m.edge,.005,6);
 for(const x of [-.355,.279])box(g,.014,.243,.579,x,.251,-.354,m.edge,.004);
 box(g,.641,.244,.024,-.038,.252,-.636,m.edge,.004);
 // The power inlet and rocker actually meet the rear opening of the chassis.
 box(g,.575,.258,.082,-.038,.251,-.710,m.edge,.004);
 const end=roundedRect(.577,.260,.022);
 slot(end,.166,.102,.009,-.168,0);slot(end,.061,.093,.009,-.036,.004);
 for(let row=0;row<3;row++)for(let col=0;col<4;col++)
  slot(end,.037,.023,.006,.047+col*.055,-.052+row*.050);
 panel(g,end,.015,-.037,.253,-.793,m.trim,0,3).rotation.y=Math.PI;
 box(g,.155,.091,.007,.131,.253,-.788,m.dark,.003);
 for(const [x,y] of [[.093,.255],[.166,.255],[.130,.280]])
  box(g,.009,.022,.013,x,y,-.797,m.tin,.001);
 box(g,.050,.078,.013,-.001,.257,-.795,m.dark,.005).rotation.x=.10;
 for(let i=0;i<7;i++)box(g,.204,.054,.012,-.042,.277,-.554+i*.061,m.trim,.001);
 const psuLid=roundedRect(.643,.579,.025);roundHole(psuLid,.190);
 panel(g,psuLid,.017,-.038,.380,-.354,m.radiator,.002,8).rotation.x=-Math.PI/2;
 rotatingFan(g,m,{id:'psu',x:-.038,y:.377,z:-.354,axis:'up',radius:.178,depth:.013,count:7,rate:.62});
 sidePanel(g,roundedRect(.481,.145,.015),.003,-.363,.277,-.361,m.trim,.001,6);
 for(const z of [-.574,-.149])for(const y of [.229,.324])screw(g,-.368,y,z,.008,m.tin,m.dark);
 for(let i=0;i<14;i++)box(g,.003,.029,.009,-.367,.252,-.532+i*.027,m.dark,.001);
 box(g,.003,.006,.394,-.368,.301,-.358,m.radiator,.001);
 for(const z of [-.571,-.128])screw(g,-.365,.180,z,.010,m.tin,m.dark);
 box(g,.520,.213,.037,-.035,.252,-.047,m.radiator,.005);
 for(const x of [-.208,-.089,.045,.177])for(const y of [.224,.294]){
  box(g,.088,.041,.014,x,y,-.023,m.dark,.002);
  for(let i=0;i<4;i++)box(g,.006,.003,.006,x+(i-1.5)*.015,y,-.014,m.trim);
 }
 for(const y of [.194,.310]){
  plate(g,.465,.360,.038,.055,.032,y,.395,m.radiator,.0045,6);
  box(g,.478,.046,.037,.031,y,.594,m.edge,.004);
  box(g,.041,.011,.014,.197,y,.615,m.diffuser,.002);
 }
 const side=roundedRect(1.406,.344,.019);
 slot(side,.560,.189,.028,-.362,-.010);
 for(let i=0;i<13;i++)slot(side,.010,.054,.004,-.033+i*.022,.068);
 sidePanel(g,side,.035,-.458,.320,0,m.radiator,0,4);
 const lip=roundedRect(.611,.235,.024);slot(lip,.559,.191,.026,0,0);
 sidePanel(g,lip,.007,-.480,.31,-.362,m.trim,0,5);
 const deck=roundedRect(.891,1.415,.038);
 for(let i=0;i<13;i++)slot(deck,.018,.243,.008,-.336+i*.052,.345);
 slot(deck,.242,.124,.047,-.152,-.455);
 slot(deck,.138,.168,.046,.302,-.523);
 panel(g,deck,.032,-.004,.495,0,m.radiator,0,5).rotation.x=-Math.PI/2;
 for(const [x,z,w,d] of [[-.152,.455,.276,.151],[.302,.523,.166,.194]]){
  const rim=roundedRect(w,d,.047);slot(rim,w-.049,d-.048,.032,0,0);
  panel(g,rim,.014,x,.519,z,m.edge,.001,4).rotation.x=-Math.PI/2;
 }
 for(const z of [-.653,.678])for(const x of [-.381,.374])screw(g,x,.515,z,.012,m.tin,m.dark,'top');
 // Rolled edge and removable grille frame around the shroud ventilation slots.
 const filter=roundedRect(.741,.304,.026);slot(filter,.697,.261,.018,0,0);
 panel(g,filter,.010,-.025,.516,.345,m.stamped,0,6).rotateX(-Math.PI/2);
 for(const z of [.200,.492])for(const x of [-.328,.277])screw(g,x,.522,z,.008,m.tin,m.dark,'top');
 for(const x of [-.433,.433])box(g,.015,.018,1.328,x,.486,0,m.coating,.003);
}
function loom(g,m){
 // Parallel insulated conductors visibly connect to their sockets and pass through the shroud.
 const power=m.edge,thread=m.cableJacket;
 box(g,.088,.067,.113,.321,1.753,.547,m.edge,.007);
 box(g,.044,.036,.106,.273,1.752,.550,m.radiator,.005);
 for(let i=0;i<11;i++){
  const o=(i-5)*.0094;
  cable(g,[[.270,1.751+o,.544],[.219,1.751+o,.593],[.166,1.672+o,.637],[.192,1.536+o,.665],[.349,1.473+o,.686]],.0055,i%3===0?thread:power,15,5);
 }
 box(g,.030,.116,.023,.176,1.614,.650,m.radiator,.003);
 box(g,.139,.044,.083,-.220,1.159,.416,m.edge,.006);
 box(g,.138,.021,.063,-.218,1.187,.429,m.radiator,.003);
 for(let i=0;i<8;i++){
  const o=(i-3.5)*.014;
  cable(g,[[-.219+o,1.172,.450],[-.272+o,1.109,.515],[-.267+o,.905,.565],[-.200+o,.703,.505],[-.151+o,.500,.456]],
   .007,i%3===0?thread:power,19,6);
 }
 const comb=box(g,.123,.028,.031,-.240,.826,.539,m.radiator,.004);comb.rotation.x=-.31;
 box(g,.050,.040,.094,.338,2.146,-.464,m.edge,.004);
 for(let i=0;i<6;i++){
  const o=(i-2.5)*.013;
  cable(g,[[.323,2.155,-.469+o],[.283,2.207,-.490+o],[.288,2.256,-.534+o],[.420,2.271,-.587+o]],.005,m.cableJacket,11,5);
 }
 for(let i=0;i<5;i++){
  const o=(i-2)*.010;
  cable(g,[[.332,.648,.453+o],[.309,.605,.468+o],[.305,.552,.501+o],[.302,.505,.533+o]],.0047,m.edge,13,5);
 }
}

function chassisFittings(g,m){
 // Folded steel tray behind the board, with rubber cable passages at its exposed edge.
 const tray=roundedRect(1.416,1.685,.042);
 for(const yy of [-.536,-.200,.157,.504])slot(tray,.087,.193,.031,.615,yy);
 sidePanel(g,tray,.022,.441,1.381,-.014,m.coating,0,6);
 for(const y of [.845,1.181,1.538,1.885]){
  const rim=roundedRect(.108,.220,.036);slot(rim,.067,.161,.022,0,0);
  sidePanel(g,rim,.018,.416,y,.601,m.gasket,.0015,7);
  for(const dz of [-.015,.015])box(g,.007,.145,.012,.407,y,.601+dz,m.soft).rotation.x=dz*7;
 }
 for(const y of [.584,2.242]){
  box(g,.043,.033,1.247,.415,y,-.022,m.stamped);
  for(const z of [-.55,-.24,.22,.56])screw(g,.389,y,z,.009,m.tin,m.dark);
 }
 // Telescoping GPU support. The rubber saddle contacts the shroud outside the fan opening.
 const x=-.322,z=.404;
 plate(g,.154,.117,.022,.018,x,.524,z,m.stamped,.002,7);
 plate(g,.122,.090,.017,.012,x,.538,z,m.gasket,.002,6);
 turned(g,[[0,-.012],[.032,-.012],[.034,0],[.025,.009],[.023,.278],[.027,.286],[.027,.305],[.017,.309],[0,.309]],x,.552,z,m.coating,26);
 cylinder(g,.015,.194,x,.944,z,m.satin,22);
 turned(g,[[.015,-.013],[.032,-.013],[.036,-.008],[.036,.012],[.032,.017],[.016,.017]],x,.858,z,m.satin,28);
 for(let i=0;i<28;i++){
  const a=i*Math.PI/14;
  box(g,.004,.019,.005,x+Math.cos(a)*.036,.860,z+Math.sin(a)*.036,m.stamped).rotation.y=-a;
 }
 plate(g,.071,.060,.013,.010,x,1.042,z,m.stamped,.002,7);
 plate(g,.065,.054,.012,.012,x,1.053,z,m.soft,.002,7);
 for(const off of [-.052,.052])screw(g,x+off,.540,z,.008,m.tin,m.dark,'top');
 // Front mounting channels and positive stops for the filter and fan carriers.
 for(const side of [-1,1]){
  const rail=roundedRect(.085,2.13,.022);
  for(let i=0;i<8;i++)slot(rail,.023,.067,.010,0,-.906+i*.259);
  panel(g,rail,.019,side*.383,1.253,.730,m.coating,0,4);
  box(g,.025,2.07,.039,side*.421,1.253,.736,m.stamped,.003);
  for(const y of [.316,.942,1.572,2.196]){
   box(g,.041,.036,.046,side*.391,y,.705,m.gasket,.005);
   sideDisk(g,.012,.011,side*.366,y,.705,m.satin,16);
  }
 }
}

function coolerHardware(g,m){
 // Thin heat sink plates fold around the fan frame; clips hook into that edge.
 for(const yy of [1.542,1.919]){
  const top=roundedRect(.221,.371,.028);
  for(const zz of [-.124,-.095,.095,.124])roundHole(top,.013,-.042,zz);
  panel(g,top,.011,.218,yy,-.190,m.satin,0,8).rotateX(-Math.PI/2);
  for(const z of [-.393,.012]){
   cable(g,[[.061,yy,z],[.098,yy+(yy>1.7 ? .017 : -.017),z],[.165,yy,z],[.186,yy,z+(z<0 ? .014 : -.014)]],.0036,m.spring,16,7);
  }
 }
 for(const z of [-.393,.012])for(const dy of [-.087,-.029,.029,.087])
  box(g,.018,.008,.008,.118,1.731+dy,z,m.stamped,.001);
 // Small tab on the 24-pin plug and folded latch on the graphics power connector.
 for(let i=0;i<10;i++)box(g,.014,.004,.006,.264,1.774,.506+i*.010,m.stamped,.001);
 box(g,.043,.011,.031,.292,1.797,.547,m.poly,.002);
 box(g,.019,.017,.027,.268,1.790,.547,m.poly,.003).rotation.z=.24;
 for(let i=0;i<8;i++)box(g,.008,.014,.025,-.269+i*.014,1.183,.466,m.stamped,.001);
 box(g,.041,.010,.045,-.226,1.200,.420,m.poly,.002).rotation.x=-.17;
 for(const [x,y,z,rz] of [[.208,1.669,.628,.18],[.200,1.554,.656,-.35],[-.232,.767,.527,-.24]]){
  const comb=roundedRect(.128,.036,.010);
  for(let i=0;i<8;i++)roundHole(comb,.0042,-.049+i*.014,0);
  const o=panel(g,comb,.009,x,y,z,m.soft,0,4);o.rotation.z=rz;
 }
 for(const z of [-.515,-.327,-.138,.049,.236,.423]){
  // Retaining tongues lock the backplate to the die-cast side rail.
  const ear=roundedRect(.064,.031,.009);roundHole(ear,.006);
  sidePanel(g,ear,.008,-.388,1.221,z,m.satin,0,5);
 }
 for(const y of [1.092,1.167]){
  sidePanel(g,roundedRect(1.024,.014,.006),.005,-.384,y,-.082,m.stamped,0,5);
 }
}

function lowerBayHardware(g,m){
 // A folded sled carries each drive; the pull loop pivots against a stamped end plate.
 for(const y of [.194,.310]){
  for(const x of [-.190,.253]){
   box(g,.020,.059,.393,x,y,.399,m.stamped,.003);
   box(g,.017,.021,.318,x-.011,y-.020,.395,m.satin);
  }
  const handle=roundedRect(.249,.038,.015);slot(handle,.202,.018,.008,0,0);
  panel(g,handle,.011,-.018,y,.620,m.poly,.002,8);
  for(const x of [-.142,.106])cylinder(g,.015,.006,x,y,.626,m.satin,16).rotateX(Math.PI/2);
  panel(g,roundedRect(.060,.024,.007),.005,.183,y,.629,m.stamped,.001,5);
 }
 // Front panel power and signal leads terminate in keyed plugs, rather than ending in mid-air.
 for(const [i,x] of [-.208,-.089,.045,.177].entries()){
  box(g,.075,.035,.042,x,.226,.008,m.poly,.003);
  box(g,.035,.008,.019,x,.247,.007,m.stamped,.002);
  for(let k=0;k<4;k++){
   const u=(k-1.5)*.014;
   cable(g,[[x+u,.226,.028],[x+u,.224,.094],[x+u*.9,.213,.144],[.040+i*.066+u,.210+i*.013,.255],[.055+i*.055+u,.193+i*.018,.352]],.0042,k===3?m.cableJacket:m.soft,18,6);
  }
 }
}
export function desktopInterior(parent,{edge,trim,dark,aluminum,portBlue,accent,fanBlade},rotors,trails){
 const std=(color,metalness,roughness)=>new THREE.MeshStandardMaterial({color,metalness,roughness});
 const m={
  edge,trim,dark,blue:portBlue,tin:aluminum,accent,caseBlade:fanBlade,rotors,trails,pcbEdge:std(0x415c52,.28,.59),
  radiator:std(0x363e47,.40,.53),fin:std(0x8a969f,.64,.48),nickel:std(0x9f816c,.68,.45),
  diffuser:new THREE.MeshStandardMaterial({color:0x85c6db,emissive:0x36768a,emissiveIntensity:.50,roughness:.43}),
  fan:std(0x546574,.32,.53),gold:std(0xaa8f61,.56,.49),cableJacket:std(0x3c4c5b,.14,.71),
  coating:std(0x292f38,.22,.64),stamped:std(0x52606d,.50,.49),satin:std(0x8b969f,.67,.38),
  spring:std(0xa4adb0,.81,.33),poly:std(0x283440,.13,.53),soft:std(0x1c242f,.04,.77),gasket:std(0x374550,.06,.78),
 };
 shroud(parent,m);
 motherboard(parent,m);
 cpuCooling(parent,m);
 graphicsCard(parent,m);
 rearHardware(parent,m);
 loom(parent,m);
 chassisFittings(parent,m);
 coolerHardware(parent,m);
 lowerBayHardware(parent,m);
}
