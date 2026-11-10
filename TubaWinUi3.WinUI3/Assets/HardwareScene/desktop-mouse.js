import * as THREE from 'three';
import {batchStatic,box,cable,cylinder,panel,plate,roundedRect,turned} from './geometry.js';

// The finger end is local -Z, towards the monitor; the palm hump is on +Z.
const stations=[
 [-.230,.055,.044,.003],[-.214,.097,.061,.002],[-.181,.121,.079,.001],
 [-.139,.132,.098,0],[-.093,.134,.116,.002],[-.045,.135,.128,.005],
 [.012,.145,.139,.008],[.066,.147,.140,.007],[.123,.139,.125,.003],
 [.174,.117,.103,-.001],[.212,.083,.071,-.004],[.237,.029,.039,-.006],[.241,.006,.031,-.006],
];
const slopes=stations.map((s,i)=>s.slice(1).map((value,j)=>{
 const a=stations[i-1],b=stations[i+1];
 if(!a)return (b[j+1]-value)/(b[0]-s[0]);
 if(!b)return (value-a[j+1])/(s[0]-a[0]);
 const left=s[0]-a[0],right=b[0]-s[0],d0=(value-a[j+1])/left,d1=(b[j+1]-value)/right;
 if(d0*d1<=0)return 0;
 // Monotone cubic slopes keep the moulded shell smooth without overshooting a station.
 const w0=2*right+left,w1=right+2*left;
 return (w0+w1)/(w0/d0+w1/d1);
}));
function section(z){
 const end=stations.findIndex(s=>s[0]>=z);
 if(end<=0)return end===0?stations[0]:stations.at(-1);
 const a=stations[end-1],b=stations[end],h=b[0]-a[0],t=(z-a[0])/h,t2=t*t,t3=t2*t;
 return [z,...a.slice(1).map((v,i)=>(2*t3-3*t2+1)*v+(t3-2*t2+t)*h*slopes[end-1][i]+
  (-2*t3+3*t2)*b[i+1]+(t3-t2)*h*slopes[end][i])];
}
function height(z,x){
 const [,w,h,c]=section(z),u=THREE.MathUtils.clamp((x-c)/w,-1,1);
 return .031+(h-.031)*Math.sqrt(1-u*u)+.008*u*Math.sqrt(1-u*u);
}
function surface(g,z0,z1,range,rows,cols,mat){
 const positions=[],uv=[],indices=[];
 for(let i=0;i<=rows;i++){
  const z=THREE.MathUtils.lerp(z0,z1,i/rows),[,w,,center]=section(z);
  const [left,right]=range(z,w);
  for(let j=0;j<=cols;j++){
   // More samples near the shoulders for the rolled side wall.
   const s=(1-Math.cos(Math.PI*j/cols))/2;
   const x=THREE.MathUtils.lerp(left,right,s)+center;
   positions.push(x,height(z,x),z);uv.push(j/cols,i/rows);
   if(i<rows&&j<cols){const a=i*(cols+1)+j;indices.push(a,a+cols+1,a+1,a+1,a+cols+1,a+cols+2);}
  }
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));
 geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();
 const mesh=new THREE.Mesh(geometry,mat);g.add(mesh);return mesh;
}
function footprint(scale=1){
 const shape=new THREE.Shape();
 const outline=Array.from({length:43},(_,i)=>section(THREE.MathUtils.lerp(stations[0][0],stations.at(-1)[0],i/42)));
 for(const side of [1,-1]){
  const rows=side===1?outline:[...outline].reverse();
  for(const [i,[z,w,,center]] of rows.entries()){
   const x=(center+side*w)*scale;
   if(!i&&side===1)shape.moveTo(x,-z*scale);else shape.lineTo(x,-z*scale);
  }
 }
 shape.closePath();return shape;
}
function trimLine(g,points,r,material){return cable(g,points,r,material,22,7);}

export function desktopMouse(parent,x,z,{edge,keys,dark,aluminum,trim,lens,accent}){
 const g=new THREE.Group();g.position.set(x,0,z);parent.add(g);
 const polymer=new THREE.MeshStandardMaterial({color:0x8b99a6,metalness:.13,roughness:.57});
 const button=new THREE.MeshStandardMaterial({color:0x748797,metalness:.18,roughness:.48});
 const elastomer=new THREE.MeshStandardMaterial({color:0x364451,roughness:.84});
 const skate=new THREE.MeshStandardMaterial({color:0x9ca8af,roughness:.48});
 panel(g,footprint(.98),.017,0,.013,0,edge,.003,4).rotation.x=-Math.PI/2;
 panel(g,footprint(1.00),.010,0,.027,0,elastomer,.002,4).rotation.x=-Math.PI/2;
 const gap=z=>.006+.018*Math.exp(-((z+.126)**2)/.0015);
 surface(g,-.038,.240,(z,w)=>[-w,w],30,24,polymer);
 surface(g,-.227,-.041,(z,w)=>[-w,-gap(z)],26,17,button);
 surface(g,-.227,-.041,(z,w)=>[gap(z),w],26,17,button);
 // The lower click cradle is visible only through the seam and wheel recess.
 const cradle=surface(g,-.228,-.033,(z,w)=>[-w*.97,w*.97],24,18,dark);
 cradle.position.y=-.006;
 for(const sign of [-1,1]){
  const guard=[-.219,-.177,-.105,-.059,.012,.078,.148,.208,.232].map(t=>{
   const [,w,,center]=section(t);return [center+sign*(w-.001),.032,t];
  });
  trimLine(g,guard,.0017,trim);
  const boundary=[-.226,-.188,-.146,-.119,-.085,-.045].map(t=>{
   const x=section(t)[3]+sign*gap(t);return [x,height(t,x)-.001,t];
  });
  trimLine(g,boundary,.0016,edge);
 }
 const wheelZ=-.128,wheelY=.089,r=.027;
 const wheel=turned(g,[[.008,-.019],[.021,-.019],[.026,-.014],[.027,-.011],[.027,.011],[.026,.014],
   [.021,.019],[.008,.019]],0,wheelY,wheelZ,aluminum,48);
 wheel.rotation.z=Math.PI/2;
 cylinder(g,.024,.022,0,wheelY,wheelZ,keys,48).rotation.z=Math.PI/2;
 for(let i=0;i<34;i++){
  const a=i*Math.PI*2/34;
  const ridge=box(g,.020,.0018,.00185,0,wheelY+Math.cos(a)*r,wheelZ+Math.sin(a)*r,trim);
  ridge.rotation.x=a;
 }
 const dpiY=height(-.023,0)+.0015;
 plate(g,.025,.019,.008,.004,0,dpiY,-.023,edge,.001,7);
 cylinder(g,.0032,.002,0,height(.003,0)+.001,.003,accent,12);
 // Moulded thumb grip and the two separate side switches are on the left.
 for(let i=0;i<9;i++){
  const z=-.033+i*.019,w=section(z)[1],c=section(z)[3],gx=c-w+.001;
  trimLine(g,[[gx,.037,z-.003],[gx-.002,.049,z],[gx+.001,.059,z+.006]],.0015,trim);
 }
 for(const [zz,angle] of [[-.101,.12],[-.047,.03]]){
  const [,w,,center]=section(zz);
  const sw=panel(g,roundedRect(.044,.014,.006),.006,center-w-.001,.059,zz,elastomer,.002,7);
  sw.rotation.y=-Math.PI/2;sw.rotation.z=angle;
 }
 const nose=roundedRect(.112,.028,.011);nose.holes.push(roundedRect(.054,.019,.008,0,.002,new THREE.Path()));
 panel(g,nose,.006,.003,.038,-.228,elastomer,.001,7).rotation.y=Math.PI;
 const port=roundedRect(.057,.021,.009);port.holes.push(roundedRect(.041,.013,.006,0,0,new THREE.Path()));
 panel(g,port,.007,.003,.040,-.231,trim,0,7).rotation.y=Math.PI;
 panel(g,roundedRect(.041,.013,.006),.003,.003,.040,-.226,dark,0,7).rotation.y=Math.PI;
 box(g,.029,.003,.006,.003,.040,-.232,keys,.001);
 for(const side of [-1,1])for(const [zz,w,d] of [[-.148,.064,.089],[.137,.071,.097]]){
  const foot=plate(g,w,d,.029,.004,side*.066,.004,zz,skate,.001,9);
  foot.rotation.y=side*(zz<0 ? .13 : -.17);
 }
 const sensor=roundedRect(.071,.080,.026);sensor.holes.push(roundedRect(.025,.034,.009,0,0,new THREE.Path()));
 panel(g,sensor,.004,0,.005,.022,dark,0,7).rotation.x=-Math.PI/2;
 cylinder(g,.011,.004,0,.004,.022,lens,24);
 plate(g,.030,.055,.011,.003,-.063,.005,-.052,trim,0,7);
 plate(g,.016,.027,.007,.004,-.063,.003,-.056,edge,.001,6);
 cylinder(g,.008,.004,.061,.005,-.051,elastomer,16);
 batchStatic(g);return g;
}
