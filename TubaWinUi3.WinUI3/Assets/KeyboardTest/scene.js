import * as THREE from 'three';
import { createKeyboardScene } from './scene-controller.js';

const renderer=new THREE.WebGLRenderer({antialias:true,alpha:true});
renderer.setClearColor(0,0);renderer.setPixelRatio(Math.min(devicePixelRatio,2));
renderer.shadowMap.enabled=true;renderer.shadowMap.type=THREE.PCFSoftShadowMap;
renderer.shadowMap.autoUpdate=false;
renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.NoToneMapping;
renderer.domElement.setAttribute('aria-label','3D 键盘，按下实体键盘进行检测，点击键帽可预览镜头');
document.body.prepend(renderer.domElement);
let frame=0,disposed=false;
function requestFrame(){if(!frame&&!disposed&&!document.hidden)frame=requestAnimationFrame(tick);}
function tick(now){frame=0;if(disposed||document.hidden)return;const moving=keyboard.update(now);renderer.render(keyboard.scene,keyboard.camera);if(moving)requestFrame();}
const keyboard=createKeyboardScene({renderer,invalidate:requestFrame,post:message=>window.chrome?.webview?.postMessage(message)});
function resize(){renderer.setSize(innerWidth,innerHeight);keyboard.resize(innerWidth,innerHeight);requestFrame();}
resize();
renderer.domElement.addEventListener('webglcontextlost',e=>{e.preventDefault();window.chrome?.webview?.postMessage({type:'error'});});
window.chrome?.webview?.addEventListener('message',e=>keyboard.receive(e.data));
addEventListener('resize',resize);
document.addEventListener('visibilitychange',()=>{keyboard.setActive(!document.hidden);if(document.hidden){cancelAnimationFrame(frame);frame=0;}else requestFrame();});
addEventListener('pagehide',()=>{disposed=true;cancelAnimationFrame(frame);keyboard.dispose();renderer.dispose();});
document.getElementById('loading').remove();
window.keyboardTest={receive:keyboard.receive,getState:()=>({...keyboard.getState(),frame,drawCalls:renderer.info.render.calls})};
window.chrome?.webview?.postMessage({type:'ready'});
