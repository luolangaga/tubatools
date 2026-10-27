// A finite celebration over the retained model. No opaque backdrop or render loop at rest.
export class InspectionCompletionEffect {
 constructor(){
  this.canvas=document.createElement('canvas');this.canvas.setAttribute('aria-hidden','true');
  this.canvas.style.cssText='position:absolute;inset:0;width:100%;height:100%;pointer-events:none;display:none';
  document.body.append(this.canvas);this.active=false;this.running=false;this.particles=[];
 }
 start({reduced=false,dark=false}={}){
  this.active=true;this.reduced=reduced;this.dark=dark;this.started=performance.now();
  this.running=!reduced;this.canvas.style.display='block';this.particles=[];
  if(reduced)return;
  let seed=731;const random=()=>{seed=(1664525*seed+1013904223)>>>0;return seed/4294967296;};
  const colors=['#72cdb0','#e6c174','#d98d87','#a99bce','#ece5d6'];
  for(let i=0;i<88;i++){
   const side=i%2?1:-1;
   this.particles.push({side,delay:random()*.42,vx:-side*(85+random()*150),vy:-90-random()*150,
    size:4+random()*4,spin:(random()-.5)*9,angle:random()*Math.PI,color:colors[i%colors.length],sway:random()*6.28});
  }
 }
 stop(){this.active=this.running=false;this.particles=[];this.canvas.style.display='none';}
 update(now){
  if(!this.active)return false;
  const width=Math.max(1,innerWidth),height=Math.max(1,innerHeight),ratio=Math.min(2,devicePixelRatio||1);
  if(this.canvas.width!==Math.round(width*ratio)||this.canvas.height!==Math.round(height*ratio)){
   this.canvas.width=Math.round(width*ratio);this.canvas.height=Math.round(height*ratio);
  }
  const ctx=this.canvas.getContext('2d');ctx.setTransform(ratio,0,0,ratio,0,0);ctx.clearRect(0,0,width,height);
  const elapsed=Math.max(0,(now-this.started)/1000),arrival=this.reduced?1:Math.min(1,elapsed/.9);
  // A quiet elliptical contact glow, with a single settling ripple.
  ctx.save();ctx.translate(width*.5,height*.89);ctx.scale(1,.16);
  const radius=Math.min(width*.28,height*.78),halo=ctx.createRadialGradient(0,0,radius*.25,0,0,radius);
  halo.addColorStop(0,'rgba(115,199,165,0)');halo.addColorStop(.65,`rgba(115,199,165,${arrival*(this.dark?.13:.09)})`);halo.addColorStop(1,'rgba(115,199,165,0)');
  ctx.fillStyle=halo;ctx.beginPath();ctx.arc(0,0,radius,0,Math.PI*2);ctx.fill();
  if(!this.reduced&&elapsed<1.6){
   const progress=elapsed/1.6;ctx.strokeStyle=`rgba(151,211,184,${(1-progress)*.25})`;ctx.lineWidth=1;
   ctx.beginPath();ctx.arc(0,0,radius*(.6+.4*progress),0,Math.PI*2);ctx.stroke();
  }
  ctx.restore();
  if(!this.reduced&&elapsed<3.8)for(const p of this.particles){
   const t=elapsed-p.delay;if(t<0)continue;
   const x=width*(p.side>0?.88:.12)+p.vx*(1-Math.exp(-t*.55))/.55+Math.sin(t*3+p.sway)*12;
   const y=height*.16+p.vy*t+165*t*t;
   if(y>height+20)continue;
   ctx.save();ctx.globalAlpha=Math.min(1,Math.max(0,(3.8-elapsed)/.8));ctx.translate(x,y);ctx.rotate(p.angle+p.spin*t);
   ctx.scale(.35+.65*Math.abs(Math.cos(p.sway+t*5)),1);ctx.fillStyle=p.color;
   ctx.fillRect(-p.size/2,-p.size*.3,p.size,p.size*.6);ctx.restore();
  }
  this.running=!this.reduced&&elapsed<3.8;
  if(!this.running)this.particles=[];
  return this.running;
 }
 state(){return {active:this.active,running:this.running,reduced:!!this.reduced,particles:this.particles.length};}
 dispose(){this.stop();this.canvas.remove();}
}
