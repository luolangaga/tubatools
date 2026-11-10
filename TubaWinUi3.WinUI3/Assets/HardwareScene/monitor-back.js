import * as THREE from 'three';
import { batchStatic, box, cylinder, panel, roundedRect, turned } from './geometry.js';

// Layout of the down-facing sockets on a narrow-frame desktop display.
export function monitorBack(parent,w,x,y,z,{shell,dark,metal,trim,portBlue}){
 const g=new THREE.Group();g.position.set(x,y,z);g.scale.setScalar(w/2.64);parent.add(g);
 const back=roundedRect(2.43,1.331,.104);
 for(const side of [-1,1])back.holes.push(roundedRect(.293,.066,.012,side*.967,-.401,new THREE.Path()));
 panel(g,back,.031,0,-.010,-.034,shell,.003,10);
 const housing=roundedRect(1.525,.915,.139);
 housing.holes.push(roundedRect(1.371,.089,.028,0,.331,new THREE.Path()));
 panel(g,housing,.068,0,-.096,-.064,trim,.009,9);
 const vent=roundedRect(1.412,.124,.049);
 for(let i=0;i<23;i++)vent.holes.push(roundedRect(.018,.065,.008,-.602+i*.0547,0,new THREE.Path()));
 panel(g,vent,.012,0,.235,-.110,trim,0,4).rotation.y=Math.PI;
 box(g,1.318,.082,.005,0,.235,-.081,dark,.003);
 const ports=roundedRect(1.280,.199,.031),layout=[[-.463,.185,.094],[-.218,.139,.062],[.020,.149,.065],[.240,.094,.039],[.453,.122,.059]];
 for(const [px,pw,ph] of layout)ports.holes.push(roundedRect(pw,ph,.007,px,0,new THREE.Path()));
 const bay=panel(g,ports,.018,0,-.472,-.075,dark,0,4);bay.rotation.x=Math.PI/2;
 for(const [i,[px,pw,ph]] of layout.entries()){
  const rim=roundedRect(pw,ph,.008);rim.holes.push(roundedRect(pw-.013,ph-.012,.005,0,0,new THREE.Path()));
  panel(g,rim,.016,px,-.479,-.075,metal,0,4).rotation.x=Math.PI/2;
  box(g,pw*.71,.007,ph*.34,px,-.469,-.076,i===4?portBlue:trim,.001);
 }
 for(const [px,py] of [[-.17,-.065],[.17,-.065],[-.17,-.359],[.17,-.359]]){
  const bore=turned(g,[[.012,-.003],[.024,-.003],[.024,.003],[.011,.003],[.012,-.003]],px,py,-.113,metal,20);
  bore.rotation.x=-Math.PI/2;
  cylinder(g,.010,.003,px,py,-.114,dark,12).rotation.x=-Math.PI/2;
 }
 const reveal=roundedRect(.505,.362,.039);
 reveal.holes.push(roundedRect(.474,.332,.032,0,0,new THREE.Path()));
 panel(g,reveal,.005,0,-.209,-.112,dark,0,7);
 for(const side of [-1,1]){
  const intake=roundedRect(.316,.083,.020);
  for(let i=0;i<8;i++)intake.holes.push(roundedRect(.021,.039,.008,(i-3.5)*.034,0,new THREE.Path()));
  panel(g,intake,.011,side*.967,-.411,-.056,shell,0,4);
  box(g,.286,.051,.004,side*.967,-.411,-.037,dark,.003);
  for(const off of [-.122,.122]){
   cylinder(g,.009,.003,side*.966+off,-.475,-.056,metal,12).rotation.x=-Math.PI/2;
   box(g,.012,.002,.002,side*.966+off,-.475,-.058,dark).rotation.z=.44;
  }
 }
 panel(g,roundedRect(.061,.039,.006),.002,.672,-.333,-.116,dark,0,4);
 const recess=roundedRect(.069,.068,.020);recess.holes.push(roundedRect(.047,.048,.016,0,0,new THREE.Path()));
 panel(g,recess,.005,.570,-.410,-.117,shell,0,7);
 const joystick=turned(g,[[0,0],[.022,0],[.022,.011],[.015,.018],[0,.018]],.570,-.410,-.113,metal,18);
 joystick.rotation.x=-Math.PI/2;
 batchStatic(g);return g;
}
