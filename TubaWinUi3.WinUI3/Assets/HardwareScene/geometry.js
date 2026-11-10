import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

export function box(parent,w,h,d,x,y,z,material,r=0){
 const radius=Math.min(r,w*.49,h*.49,d*.49);
 const geometry=radius>0?new RoundedBoxGeometry(w,h,d,1,radius):new THREE.BoxGeometry(w,h,d);
 const mesh=new THREE.Mesh(geometry,material);
 mesh.position.set(x,y,z);parent.add(mesh);return mesh;
}

// Fold fixed parts into their parent (lid, chassis or rotor), never across a moving joint.
// One draw per material keeps small vents, fasteners and connectors inexpensive.
export function batchStatic(parent){
 const batches=new Map();
 for(const mesh of parent.children){
  if(!mesh.isMesh||mesh.isInstancedMesh||Array.isArray(mesh.material))continue;
  if(!batches.has(mesh.material))batches.set(mesh.material,[]);
  batches.get(mesh.material).push(mesh);
 }
 for(const [material,meshes] of batches){
  if(meshes.length<2)continue;
  const parts=meshes.map(mesh=>{
   mesh.updateMatrix();
   return (mesh.geometry.index?mesh.geometry.toNonIndexed():mesh.geometry.clone()).applyMatrix4(mesh.matrix);
  });
  const geometry=mergeGeometries(parts);
  for(const part of parts)part.dispose();
  if(!geometry)continue;
  for(const mesh of meshes){parent.remove(mesh);mesh.geometry.dispose();}
  parent.add(new THREE.Mesh(geometry,material));
 }
}

export function cylinder(parent,r,h,x,y,z,material,segments=16){
 const mesh=new THREE.Mesh(new THREE.CylinderGeometry(r,r,h,segments),material);
 mesh.position.set(x,y,z);parent.add(mesh);return mesh;
}

// Profile radius and edge bevel are independent: thin sheet parts can still have broad corners.
export function roundedRect(w,h,r,x=0,y=0,path=new THREE.Shape()){
 const l=x-w/2,b=y-h/2,R=l+w,T=b+h;
 r=Math.min(r,w/2,h/2);
 path.moveTo(l+r,b);path.lineTo(R-r,b);path.quadraticCurveTo(R,b,R,b+r);
 path.lineTo(R,T-r);path.quadraticCurveTo(R,T,R-r,T);
 path.lineTo(l+r,T);path.quadraticCurveTo(l,T,l,T-r);
 path.lineTo(l,b+r);path.quadraticCurveTo(l,b,l+r,b);path.closePath();return path;
}

export function roundHole(shape,r,x=0,y=0){
 const hole=new THREE.Path();hole.absarc(x,y,r,0,Math.PI*2,true);shape.holes.push(hole);return hole;
}

export function panel(parent,shape,depth,x,y,z,material,bevel=0,segments=6,bevelSegments=2){
 const geometry=new THREE.ExtrudeGeometry(shape,{depth,curveSegments:segments,bevelEnabled:bevel>0,
  bevelSize:bevel,bevelThickness:bevel,bevelSegments});
 geometry.clearGroups();geometry.translate(0,0,-depth/2);
 const mesh=new THREE.Mesh(geometry,material);
 mesh.position.set(x,y,z);parent.add(mesh);return mesh;
}

export function plate(parent,w,d,r,depth,x,y,z,material,bevel=0,segments=6){
 const mesh=panel(parent,roundedRect(w,d,r),depth,x,y,z,material,bevel,segments);
 mesh.rotation.x=-Math.PI/2;return mesh;
}

// Axial profile, including open bores, rolled rims and counterbores. Axis is +Y.
export function turned(parent,profile,x,y,z,material,segments=32){
 const geometry=new THREE.LatheGeometry(profile.map(([r,a])=>new THREE.Vector2(r,a)),segments);
 const mesh=new THREE.Mesh(geometry,material);mesh.position.set(x,y,z);parent.add(mesh);return mesh;
}

export function cable(parent,points,r,material,segments=28,sides=7){
 const path=new THREE.CatmullRomCurve3(points.map(([x,y,z])=>new THREE.Vector3(x,y,z)));
 const mesh=new THREE.Mesh(new THREE.TubeGeometry(path,segments,r,sides,false),material);
 parent.add(mesh);return mesh;
}

// One swept, thin impeller per pose, with roots overlapping the hub and tips inside the frame.
export function impeller(radius,depth,count){
 const blade=new THREE.Shape();
 blade.moveTo(.18,-.05);
 blade.quadraticCurveTo(.41,-.24,.82,-.45);
 blade.quadraticCurveTo(.93,-.4,.97,-.16);
 blade.quadraticCurveTo(.66,.18,.21,.15);
 blade.closePath();
 const geometry=new THREE.ExtrudeGeometry(blade,{depth:depth/radius,bevelEnabled:false,curveSegments:5});
 geometry.scale(radius,radius,radius);
 geometry.translate(0,0,-depth/2);
 const parts=Array.from({length:count},(_,i)=>geometry.clone().rotateZ(i*Math.PI*2/count));
 const result=mergeGeometries(parts);
 for(const part of parts)part.dispose();
 geometry.dispose();return result;
}
