import * as THREE from 'three';

// Lightweight visual feedback over the original computer. No new models, sensor reads
// or simulated temperatures: the native stress runner supplies its existing samples.
export class HardwareStressEffect {
 constructor(getScreen, studio, fill) {
  this.getScreen=getScreen;this.studio=studio;this.fill=fill;this.active=false;
  this.overlay=document.createElement('canvas');
  this.overlay.setAttribute('aria-hidden','true');
  this.overlay.style.cssText='position:absolute;inset:0;width:100%;height:100%;pointer-events:none;display:none';
  document.body.append(this.overlay);
 }
 setActive(active) {
  if(this.active===active)return;
  this.active=active;
  if(active){
   this.started=performance.now();this.lastScreen=0;this.screenFrames=0;this.samples=[];
   this.sample={cpuTemp:-1,gpuTemp:-1,cpuLoad:-1,gpuLoad:-1};
   this.canvas=document.createElement('canvas');this.canvas.width=1024;this.canvas.height=640;
   this.texture=new THREE.CanvasTexture(this.canvas);this.texture.colorSpace=THREE.SRGBColorSpace;
   const screen=this.getScreen();this.original=screen.map;screen.map=this.texture;screen.needsUpdate=true;
   this.studioColor=this.studio.color.clone();this.fillColor=this.fill.color.clone();
   this.studio.color.set(0xffd5a0);this.fill.color.set(0xffa660);
   this.overlay.style.display='block';
  }else{
   const screen=this.getScreen();
   if(screen?.map===this.texture){screen.map=this.original;screen.needsUpdate=true;}
   this.texture?.dispose();this.texture=null;this.original=null;this.canvas=null;
   this.studio.color.copy(this.studioColor);this.fill.color.copy(this.fillColor);
   this.overlay.style.display='none';
  }
 }
 setSample(sample) {
  if(!this.active)return;
  this.sample=sample;this.samples.push({cpu:sample.cpuLoad,gpu:sample.gpuLoad});
  if(this.samples.length>60)this.samples.shift();
  this.lastScreen=0;
 }
 update(now,anchors,reduced) {
  if(!this.active)return;
  const elapsed=(now-this.started)/1000;
  if(now-this.lastScreen>250){this.paintScreen(elapsed,reduced);this.lastScreen=now;}
  const w=Math.max(1,Math.round(innerWidth)),h=Math.max(1,Math.round(innerHeight));
  if(this.overlay.width!==w||this.overlay.height!==h){this.overlay.width=w;this.overlay.height=h;}
  const ctx=this.overlay.getContext('2d');ctx.clearRect(0,0,w,h);
  const anchor=anchors['散热']||[.5,.7],x=Math.max(.15,Math.min(.85,anchor[0]))*w,y=Math.max(.35,Math.min(.92,anchor[1]))*h;
  const strength=.45+.25*Math.min(1,elapsed/15);
  const glow=ctx.createRadialGradient(x,y,0,x,y,w*.8);
  glow.addColorStop(0,`rgba(255,139,42,${strength*.16})`);glow.addColorStop(1,'rgba(255,190,80,0)');
  ctx.fillStyle=glow;ctx.fillRect(0,0,w,h);
  // Slow rising mirage ribbons, rather than flashing flames or opaque smoke.
  if(!reduced)for(let i=0;i<8;i++){
   const phase=(elapsed*.26+i*.137)%1,spread=(i-3.5)*w*.047;
   const rise=phase*h*.42,baseX=x+spread+Math.sin(elapsed*1.4+i)*8;
   ctx.beginPath();ctx.moveTo(baseX,y-rise);
   ctx.bezierCurveTo(baseX-14,y-rise-20,baseX+16,y-rise-48,baseX+Math.sin(elapsed+i)*10,y-rise-76);
   ctx.strokeStyle=`rgba(255,205,131,${Math.sin(phase*Math.PI)*strength*.3})`;
   ctx.lineWidth=1.3+(i%3)*.5;ctx.stroke();
  }
 }
 paintScreen(elapsed,reduced) {
  const c=this.canvas.getContext('2d'),w=1024,h=640;
  const gradient=c.createLinearGradient(0,0,w,h);gradient.addColorStop(0,'#341c20');gradient.addColorStop(.5,'#7f321e');gradient.addColorStop(1,'#d77832');
  c.fillStyle=gradient;c.fillRect(0,0,w,h);
  const sunX=800+(reduced?0:Math.sin(elapsed*.2)*24);
  const sun=c.createRadialGradient(sunX,160,8,sunX,160,300);
  sun.addColorStop(0,'#ffdc80');sun.addColorStop(.22,'#ffc16c88');sun.addColorStop(1,'#ffaf4300');
  c.fillStyle=sun;c.fillRect(0,0,w,h);
  c.fillStyle='#ffe9c8';c.font='500 24px Segoe UI';c.fillText('TUBA / STABILITY CHECK',56,62);
  c.font='600 56px Segoe UI';c.fillText('烤机进行中',56,140);
  c.font='24px Consolas';c.fillText(`${Math.floor(elapsed/60).toString().padStart(2,'0')}:${Math.floor(elapsed%60).toString().padStart(2,'0')}`,56,188);
  const value=(v,unit)=>Number.isFinite(v)&&v>=0?`${Math.round(v)}${unit}`:'--';
  const values=[['CPU 温度',value(this.sample.cpuTemp,'°C')],['GPU 温度',value(this.sample.gpuTemp,'°C')],['CPU 负载',value(this.sample.cpuLoad,'%')],['GPU 负载',value(this.sample.gpuLoad,'%')]];
  for(let i=0;i<values.length;i++){
   const x=56+(i%2)*440,y=260+Math.floor(i/2)*110;
   c.fillStyle='#f9cda9';c.font='22px Segoe UI';c.fillText(values[i][0],x,y);
   c.fillStyle='#fff0d6';c.font='600 44px Segoe UI';c.fillText(values[i][1],x,y+48);
  }
  c.strokeStyle='#ffd69666';c.lineWidth=1;c.beginPath();c.moveTo(56,580);c.lineTo(960,580);c.stroke();
  for(const [key,color] of [['cpu','#ffdc91'],['gpu','#ffa17c']]){
   c.strokeStyle=color;c.lineWidth=3;c.beginPath();let connected=false;
   for(let i=0;i<this.samples.length;i++){
    const value=this.samples[i][key];if(!(value>=0)){connected=false;continue;}
    const x=56+i*904/59,y=574-Math.min(100,value)*.7;
    if(connected)c.lineTo(x,y);else c.moveTo(x,y);connected=true;
   }c.stroke();
  }
  // A quiet moving progress accent makes activity visible even before sensors are ready.
  c.fillStyle='#ffe1a0';c.fillRect(56+(reduced?0:elapsed*.12%1)*824,606,80,3);
  this.texture.needsUpdate=true;this.screenFrames++;
 }
 state(){return {active:this.active,screenFrames:this.screenFrames??0,sample:this.sample??null};}
}
