(()=>{var ws={LEFT:0,MIDDLE:1,RIGHT:2,ROTATE:0,DOLLY:1,PAN:2},As={ROTATE:0,PAN:1,DOLLY_PAN:2,DOLLY_ROTATE:3},Ff=0,qh=1,Uf=2;var Qa=1,Fc=2,Xr=3,Ui=0,yn=1,oi=2,Oi=0,jr=1,Xh=2,jh=3,Kh=4,Of=5;var er=100,Bf=101,kf=102,zf=103,Hf=104,Gf=200,Vf=201,Wf=202,qf=203,Yh=204,Jh=205,Xf=206,jf=207,Kf=208,Yf=209,Jf=210,Zf=211,$f=212,Qf=213,ep=214,rc=0,ac=1,oc=2,Ar=3,cc=4,lc=5,hc=6,uc=7,Uc=0,tp=1,np=2,vi=0,Zh=1,$h=2,Qh=3,eo=4,eu=5,tu=6,nu=7,Ch="attached",ip="detached",iu=300,Rs=301,tr=302,Oc=303,Bc=304,to=306,Pi=1e3,ni=1001,Rr=1002,Wt=1003,kc=1004;var nr=1005;var Xt=1006,Kr=1007;var qn=1008;var Un=1009,su=1010,ru=1011,Yr=1012,zc=1013,Mi=1014,Xn=1015,Si=1016,Hc=1017,Gc=1018,Jr=1020,au=35902,ou=35899,cu=1021,lu=1022,jn=1023,Ii=1026,Cs=1027,Vc=1028,Wc=1029,Ps=1030,qc=1031;var Xc=1033,no=33776,io=33777,so=33778,ro=33779,jc=35840,Kc=35841,Yc=35842,Jc=35843,Zc=36196,$c=37492,Qc=37496,el=37488,tl=37489,ao=37490,nl=37491,il=37808,sl=37809,rl=37810,al=37811,ol=37812,cl=37813,ll=37814,hl=37815,ul=37816,dl=37817,fl=37818,pl=37819,ml=37820,gl=37821,bl=36492,xl=36494,_l=36495,yl=36283,vl=36284,oo=36285,Ml=36286;var Vs=2300,Ws=2301,nc=2302,Ph=2303,Ih=2400,Lh=2401,Dh=2402,sp=2500;var hu=0,co=1,Zr=2,rp=3200;var lo=0,ap=1,rs="",pt="srgb",Dn="srgb-linear",Ea="linear",wt="srgb";var ic=7680;var op=519,cp=512,lp=513,hp=514,Sl=515,up=516,dp=517,El=518,fp=519,uu=35044,ho=35048;var du="300 es",xi=2e3,Cr=2001;function J0(i){for(let e=i.length-1;e>=0;--e)if(i[e]>=65535)return!0;return!1}function Z0(i){return ArrayBuffer.isView(i)&&!(i instanceof DataView)}function Pr(i){return document.createElementNS("http://www.w3.org/1999/xhtml",i)}function pp(){let i=Pr("canvas");return i.style.display="block",i}var Vd={},Ir=null;function Ta(...i){let e="THREE."+i.shift();Ir?Ir("log",e,...i):console.log(e,...i)}function mp(i){let e=i[0];if(typeof e=="string"&&e.startsWith("TSL:")){let t=i[1];t&&t.isStackTrace?i[0]+=" "+t.getLocation():i[1]='Stack trace not available. Enable "THREE.Node.captureStackTrace" to capture stack traces.'}return i}function Ve(...i){i=mp(i);let e="THREE."+i.shift();if(Ir)Ir("warn",e,...i);else{let t=i[0];t&&t.isStackTrace?console.warn(t.getError(e)):console.warn(e,...i)}}function Ye(...i){i=mp(i);let e="THREE."+i.shift();if(Ir)Ir("error",e,...i);else{let t=i[0];t&&t.isStackTrace?console.error(t.getError(e)):console.error(e,...i)}}function Gs(...i){let e=i.join(" ");e in Vd||(Vd[e]=!0,Ve(...i))}function gp(i,e,t){return new Promise(function(n,s){function r(){switch(i.clientWaitSync(e,i.SYNC_FLUSH_COMMANDS_BIT,0)){case i.WAIT_FAILED:s();break;case i.TIMEOUT_EXPIRED:setTimeout(r,t);break;default:n()}}setTimeout(r,t)})}var bp={[rc]:ac,[oc]:hc,[cc]:uc,[Ar]:lc,[ac]:rc,[hc]:oc,[uc]:cc,[lc]:Ar},_i=class{addEventListener(e,t){this._listeners===void 0&&(this._listeners={});let n=this._listeners;n[e]===void 0&&(n[e]=[]),n[e].indexOf(t)===-1&&n[e].push(t)}hasEventListener(e,t){let n=this._listeners;return n===void 0?!1:n[e]!==void 0&&n[e].indexOf(t)!==-1}removeEventListener(e,t){let n=this._listeners;if(n===void 0)return;let s=n[e];if(s!==void 0){let r=s.indexOf(t);r!==-1&&s.splice(r,1)}}dispatchEvent(e){let t=this._listeners;if(t===void 0)return;let n=t[e.type];if(n!==void 0){e.target=this;let s=n.slice(0);for(let r=0,a=s.length;r<a;r++)s[r].call(this,e);e.target=null}}},En=["00","01","02","03","04","05","06","07","08","09","0a","0b","0c","0d","0e","0f","10","11","12","13","14","15","16","17","18","19","1a","1b","1c","1d","1e","1f","20","21","22","23","24","25","26","27","28","29","2a","2b","2c","2d","2e","2f","30","31","32","33","34","35","36","37","38","39","3a","3b","3c","3d","3e","3f","40","41","42","43","44","45","46","47","48","49","4a","4b","4c","4d","4e","4f","50","51","52","53","54","55","56","57","58","59","5a","5b","5c","5d","5e","5f","60","61","62","63","64","65","66","67","68","69","6a","6b","6c","6d","6e","6f","70","71","72","73","74","75","76","77","78","79","7a","7b","7c","7d","7e","7f","80","81","82","83","84","85","86","87","88","89","8a","8b","8c","8d","8e","8f","90","91","92","93","94","95","96","97","98","99","9a","9b","9c","9d","9e","9f","a0","a1","a2","a3","a4","a5","a6","a7","a8","a9","aa","ab","ac","ad","ae","af","b0","b1","b2","b3","b4","b5","b6","b7","b8","b9","ba","bb","bc","bd","be","bf","c0","c1","c2","c3","c4","c5","c6","c7","c8","c9","ca","cb","cc","cd","ce","cf","d0","d1","d2","d3","d4","d5","d6","d7","d8","d9","da","db","dc","dd","de","df","e0","e1","e2","e3","e4","e5","e6","e7","e8","e9","ea","eb","ec","ed","ee","ef","f0","f1","f2","f3","f4","f5","f6","f7","f8","f9","fa","fb","fc","fd","fe","ff"],Wd=1234567,ya=Math.PI/180,qs=180/Math.PI;function ii(){let i=Math.random()*4294967295|0,e=Math.random()*4294967295|0,t=Math.random()*4294967295|0,n=Math.random()*4294967295|0;return(En[i&255]+En[i>>8&255]+En[i>>16&255]+En[i>>24&255]+"-"+En[e&255]+En[e>>8&255]+"-"+En[e>>16&15|64]+En[e>>24&255]+"-"+En[t&63|128]+En[t>>8&255]+"-"+En[t>>16&255]+En[t>>24&255]+En[n&255]+En[n>>8&255]+En[n>>16&255]+En[n>>24&255]).toLowerCase()}function it(i,e,t){return Math.max(e,Math.min(t,i))}function fu(i,e){return(i%e+e)%e}function $0(i,e,t,n,s){return n+(i-e)*(s-n)/(t-e)}function Q0(i,e,t){return i!==e?(t-i)/(e-i):0}function va(i,e,t){return(1-t)*i+t*e}function em(i,e,t,n){return va(i,e,1-Math.exp(-t*n))}function tm(i,e=1){return e-Math.abs(fu(i,e*2)-e)}function nm(i,e,t){return i<=e?0:i>=t?1:(i=(i-e)/(t-e),i*i*(3-2*i))}function im(i,e,t){return i<=e?0:i>=t?1:(i=(i-e)/(t-e),i*i*i*(i*(i*6-15)+10))}function sm(i,e){return i+Math.floor(Math.random()*(e-i+1))}function rm(i,e){return i+Math.random()*(e-i)}function am(i){return i*(.5-Math.random())}function om(i){i!==void 0&&(Wd=i);let e=Wd+=1831565813;return e=Math.imul(e^e>>>15,e|1),e^=e+Math.imul(e^e>>>7,e|61),((e^e>>>14)>>>0)/4294967296}function cm(i){return i*ya}function lm(i){return i*qs}function hm(i){return i>0&&Number.isInteger(i)&&2**Math.round(Math.log2(i))===i}function um(i){return Math.pow(2,Math.ceil(Math.log(i)/Math.LN2))}function dm(i){return Math.pow(2,Math.floor(Math.log(i)/Math.LN2))}function fm(i,e,t,n,s){let r=Math.cos,a=Math.sin,o=r(t/2),c=a(t/2),l=r((e+n)/2),h=a((e+n)/2),u=r((e-n)/2),d=a((e-n)/2),f=r((n-e)/2),m=a((n-e)/2);switch(s){case"XYX":i.set(o*h,c*u,c*d,o*l);break;case"YZY":i.set(c*d,o*h,c*u,o*l);break;case"ZXZ":i.set(c*u,c*d,o*h,o*l);break;case"XZX":i.set(o*h,c*m,c*f,o*l);break;case"YXY":i.set(c*f,o*h,c*m,o*l);break;case"ZYZ":i.set(c*m,c*f,o*h,o*l);break;default:Ve("MathUtils: .setQuaternionFromProperEuler() encountered an unknown order: "+s)}}function bi(i,e){switch(e.constructor){case Float32Array:return i;case Uint32Array:return i/4294967295;case Uint16Array:return i/65535;case Uint8Array:case Uint8ClampedArray:return i/255;case Int32Array:return Math.max(i/2147483647,-1);case Int16Array:return Math.max(i/32767,-1);case Int8Array:return Math.max(i/127,-1);default:throw new Error("THREE.MathUtils: Invalid component type.")}}function Ct(i,e){switch(e.constructor){case Float32Array:return i;case Uint32Array:return Math.round(i*4294967295);case Uint16Array:return Math.round(i*65535);case Uint8Array:case Uint8ClampedArray:return Math.round(i*255);case Int32Array:return Math.round(i*2147483647);case Int16Array:return Math.round(i*32767);case Int8Array:return Math.round(i*127);default:throw new Error("THREE.MathUtils: Invalid component type.")}}var Ht={DEG2RAD:ya,RAD2DEG:qs,generateUUID:ii,clamp:it,euclideanModulo:fu,mapLinear:$0,inverseLerp:Q0,lerp:va,damp:em,pingpong:tm,smoothstep:nm,smootherstep:im,randInt:sm,randFloat:rm,randFloatSpread:am,seededRandom:om,degToRad:cm,radToDeg:lm,isPowerOfTwo:hm,ceilPowerOfTwo:um,floorPowerOfTwo:dm,setQuaternionFromProperEuler:fm,normalize:Ct,denormalize:bi},ae=class i{static{i.prototype.isVector2=!0}constructor(e=0,t=0){this.x=e,this.y=t}get width(){return this.x}set width(e){this.x=e}get height(){return this.y}set height(e){this.y=e}set(e,t){return this.x=e,this.y=t,this}setScalar(e){return this.x=e,this.y=e,this}setX(e){return this.x=e,this}setY(e){return this.y=e,this}setComponent(e,t){switch(e){case 0:this.x=t;break;case 1:this.y=t;break;default:throw new Error("THREE.Vector2: index is out of range: "+e)}return this}getComponent(e){switch(e){case 0:return this.x;case 1:return this.y;default:throw new Error("THREE.Vector2: index is out of range: "+e)}}clone(){return new this.constructor(this.x,this.y)}copy(e){return this.x=e.x,this.y=e.y,this}add(e){return this.x+=e.x,this.y+=e.y,this}addScalar(e){return this.x+=e,this.y+=e,this}addVectors(e,t){return this.x=e.x+t.x,this.y=e.y+t.y,this}addScaledVector(e,t){return this.x+=e.x*t,this.y+=e.y*t,this}sub(e){return this.x-=e.x,this.y-=e.y,this}subScalar(e){return this.x-=e,this.y-=e,this}subVectors(e,t){return this.x=e.x-t.x,this.y=e.y-t.y,this}multiply(e){return this.x*=e.x,this.y*=e.y,this}multiplyScalar(e){return this.x*=e,this.y*=e,this}divide(e){return this.x/=e.x,this.y/=e.y,this}divideScalar(e){return this.multiplyScalar(1/e)}applyMatrix3(e){let t=this.x,n=this.y,s=e.elements;return this.x=s[0]*t+s[3]*n+s[6],this.y=s[1]*t+s[4]*n+s[7],this}min(e){return this.x=Math.min(this.x,e.x),this.y=Math.min(this.y,e.y),this}max(e){return this.x=Math.max(this.x,e.x),this.y=Math.max(this.y,e.y),this}clamp(e,t){return this.x=it(this.x,e.x,t.x),this.y=it(this.y,e.y,t.y),this}clampScalar(e,t){return this.x=it(this.x,e,t),this.y=it(this.y,e,t),this}clampLength(e,t){let n=this.length();return this.divideScalar(n||1).multiplyScalar(it(n,e,t))}floor(){return this.x=Math.floor(this.x),this.y=Math.floor(this.y),this}ceil(){return this.x=Math.ceil(this.x),this.y=Math.ceil(this.y),this}round(){return this.x=Math.round(this.x),this.y=Math.round(this.y),this}roundToZero(){return this.x=Math.trunc(this.x),this.y=Math.trunc(this.y),this}negate(){return this.x=-this.x,this.y=-this.y,this}dot(e){return this.x*e.x+this.y*e.y}cross(e){return this.x*e.y-this.y*e.x}lengthSq(){return this.x*this.x+this.y*this.y}length(){return Math.sqrt(this.x*this.x+this.y*this.y)}manhattanLength(){return Math.abs(this.x)+Math.abs(this.y)}normalize(){return this.divideScalar(this.length()||1)}angle(){return Math.atan2(-this.y,-this.x)+Math.PI}angleTo(e){let t=Math.sqrt(this.lengthSq()*e.lengthSq());if(t===0)return Math.PI/2;let n=this.dot(e)/t;return Math.acos(it(n,-1,1))}distanceTo(e){return Math.sqrt(this.distanceToSquared(e))}distanceToSquared(e){let t=this.x-e.x,n=this.y-e.y;return t*t+n*n}manhattanDistanceTo(e){return Math.abs(this.x-e.x)+Math.abs(this.y-e.y)}setLength(e){return this.normalize().multiplyScalar(e)}lerp(e,t){return this.x+=(e.x-this.x)*t,this.y+=(e.y-this.y)*t,this}lerpVectors(e,t,n){return this.x=e.x+(t.x-e.x)*n,this.y=e.y+(t.y-e.y)*n,this}equals(e){return e.x===this.x&&e.y===this.y}fromArray(e,t=0){return this.x=e[t],this.y=e[t+1],this}toArray(e=[],t=0){return e[t]=this.x,e[t+1]=this.y,e}fromBufferAttribute(e,t){return this.x=e.getX(t),this.y=e.getY(t),this}rotateAround(e,t){let n=Math.cos(t),s=Math.sin(t),r=this.x-e.x,a=this.y-e.y;return this.x=r*n-a*s+e.x,this.y=r*s+a*n+e.y,this}random(){return this.x=Math.random(),this.y=Math.random(),this}*[Symbol.iterator](){yield this.x,yield this.y}},ln=class{constructor(e=0,t=0,n=0,s=1){this.isQuaternion=!0,this._x=e,this._y=t,this._z=n,this._w=s}static slerpFlat(e,t,n,s,r,a,o){let c=n[s+0],l=n[s+1],h=n[s+2],u=n[s+3],d=r[a+0],f=r[a+1],m=r[a+2],b=r[a+3];if(u!==b||c!==d||l!==f||h!==m){let g=c*d+l*f+h*m+u*b;g<0&&(d=-d,f=-f,m=-m,b=-b,g=-g);let p=1-o;if(g<.9995){let _=Math.acos(g),S=Math.sin(_);p=Math.sin(p*_)/S,o=Math.sin(o*_)/S,c=c*p+d*o,l=l*p+f*o,h=h*p+m*o,u=u*p+b*o}else{c=c*p+d*o,l=l*p+f*o,h=h*p+m*o,u=u*p+b*o;let _=1/Math.sqrt(c*c+l*l+h*h+u*u);c*=_,l*=_,h*=_,u*=_}}e[t]=c,e[t+1]=l,e[t+2]=h,e[t+3]=u}static multiplyQuaternionsFlat(e,t,n,s,r,a){let o=n[s],c=n[s+1],l=n[s+2],h=n[s+3],u=r[a],d=r[a+1],f=r[a+2],m=r[a+3];return e[t]=o*m+h*u+c*f-l*d,e[t+1]=c*m+h*d+l*u-o*f,e[t+2]=l*m+h*f+o*d-c*u,e[t+3]=h*m-o*u-c*d-l*f,e}get x(){return this._x}set x(e){this._x=e,this._onChangeCallback()}get y(){return this._y}set y(e){this._y=e,this._onChangeCallback()}get z(){return this._z}set z(e){this._z=e,this._onChangeCallback()}get w(){return this._w}set w(e){this._w=e,this._onChangeCallback()}set(e,t,n,s){return this._x=e,this._y=t,this._z=n,this._w=s,this._onChangeCallback(),this}clone(){return new this.constructor(this._x,this._y,this._z,this._w)}copy(e){return this._x=e.x,this._y=e.y,this._z=e.z,this._w=e.w,this._onChangeCallback(),this}setFromEuler(e,t=!0){let n=e._x,s=e._y,r=e._z,a=e._order,o=Math.cos,c=Math.sin,l=o(n/2),h=o(s/2),u=o(r/2),d=c(n/2),f=c(s/2),m=c(r/2);switch(a){case"XYZ":this._x=d*h*u+l*f*m,this._y=l*f*u-d*h*m,this._z=l*h*m+d*f*u,this._w=l*h*u-d*f*m;break;case"YXZ":this._x=d*h*u+l*f*m,this._y=l*f*u-d*h*m,this._z=l*h*m-d*f*u,this._w=l*h*u+d*f*m;break;case"ZXY":this._x=d*h*u-l*f*m,this._y=l*f*u+d*h*m,this._z=l*h*m+d*f*u,this._w=l*h*u-d*f*m;break;case"ZYX":this._x=d*h*u-l*f*m,this._y=l*f*u+d*h*m,this._z=l*h*m-d*f*u,this._w=l*h*u+d*f*m;break;case"YZX":this._x=d*h*u+l*f*m,this._y=l*f*u+d*h*m,this._z=l*h*m-d*f*u,this._w=l*h*u-d*f*m;break;case"XZY":this._x=d*h*u-l*f*m,this._y=l*f*u-d*h*m,this._z=l*h*m+d*f*u,this._w=l*h*u+d*f*m;break;default:Ve("Quaternion: .setFromEuler() encountered an unknown order: "+a)}return t===!0&&this._onChangeCallback(),this}setFromAxisAngle(e,t){let n=t/2,s=Math.sin(n);return this._x=e.x*s,this._y=e.y*s,this._z=e.z*s,this._w=Math.cos(n),this._onChangeCallback(),this}setFromRotationMatrix(e){let t=e.elements,n=t[0],s=t[4],r=t[8],a=t[1],o=t[5],c=t[9],l=t[2],h=t[6],u=t[10],d=n+o+u;if(d>0){let f=.5/Math.sqrt(d+1);this._w=.25/f,this._x=(h-c)*f,this._y=(r-l)*f,this._z=(a-s)*f}else if(n>o&&n>u){let f=2*Math.sqrt(1+n-o-u);this._w=(h-c)/f,this._x=.25*f,this._y=(s+a)/f,this._z=(r+l)/f}else if(o>u){let f=2*Math.sqrt(1+o-n-u);this._w=(r-l)/f,this._x=(s+a)/f,this._y=.25*f,this._z=(c+h)/f}else{let f=2*Math.sqrt(1+u-n-o);this._w=(a-s)/f,this._x=(r+l)/f,this._y=(c+h)/f,this._z=.25*f}return this._onChangeCallback(),this}setFromUnitVectors(e,t){let n=e.dot(t)+1;return n<1e-8?(n=0,Math.abs(e.x)>Math.abs(e.z)?(this._x=-e.y,this._y=e.x,this._z=0,this._w=n):(this._x=0,this._y=-e.z,this._z=e.y,this._w=n)):(this._x=e.y*t.z-e.z*t.y,this._y=e.z*t.x-e.x*t.z,this._z=e.x*t.y-e.y*t.x,this._w=n),this.normalize()}angleTo(e){return 2*Math.acos(Math.abs(it(this.dot(e),-1,1)))}rotateTowards(e,t){let n=this.angleTo(e);if(n===0)return this;let s=Math.min(1,t/n);return this.slerp(e,s),this}identity(){return this.set(0,0,0,1)}invert(){return this.conjugate()}conjugate(){return this._x*=-1,this._y*=-1,this._z*=-1,this._onChangeCallback(),this}dot(e){return this._x*e._x+this._y*e._y+this._z*e._z+this._w*e._w}lengthSq(){return this._x*this._x+this._y*this._y+this._z*this._z+this._w*this._w}length(){return Math.sqrt(this._x*this._x+this._y*this._y+this._z*this._z+this._w*this._w)}normalize(){let e=this.length();return e===0?(this._x=0,this._y=0,this._z=0,this._w=1):(e=1/e,this._x=this._x*e,this._y=this._y*e,this._z=this._z*e,this._w=this._w*e),this._onChangeCallback(),this}multiply(e){return this.multiplyQuaternions(this,e)}premultiply(e){return this.multiplyQuaternions(e,this)}multiplyQuaternions(e,t){let n=e._x,s=e._y,r=e._z,a=e._w,o=t._x,c=t._y,l=t._z,h=t._w;return this._x=n*h+a*o+s*l-r*c,this._y=s*h+a*c+r*o-n*l,this._z=r*h+a*l+n*c-s*o,this._w=a*h-n*o-s*c-r*l,this._onChangeCallback(),this}slerp(e,t){let n=e._x,s=e._y,r=e._z,a=e._w,o=this.dot(e);o<0&&(n=-n,s=-s,r=-r,a=-a,o=-o);let c=1-t;if(o<.9995){let l=Math.acos(o),h=Math.sin(l);c=Math.sin(c*l)/h,t=Math.sin(t*l)/h,this._x=this._x*c+n*t,this._y=this._y*c+s*t,this._z=this._z*c+r*t,this._w=this._w*c+a*t,this._onChangeCallback()}else this._x=this._x*c+n*t,this._y=this._y*c+s*t,this._z=this._z*c+r*t,this._w=this._w*c+a*t,this.normalize();return this}slerpQuaternions(e,t,n){return this.copy(e).slerp(t,n)}random(){let e=2*Math.PI*Math.random(),t=2*Math.PI*Math.random(),n=Math.random(),s=Math.sqrt(1-n),r=Math.sqrt(n);return this.set(s*Math.sin(e),s*Math.cos(e),r*Math.sin(t),r*Math.cos(t))}equals(e){return e._x===this._x&&e._y===this._y&&e._z===this._z&&e._w===this._w}fromArray(e,t=0){return this._x=e[t],this._y=e[t+1],this._z=e[t+2],this._w=e[t+3],this._onChangeCallback(),this}toArray(e=[],t=0){return e[t]=this._x,e[t+1]=this._y,e[t+2]=this._z,e[t+3]=this._w,e}fromBufferAttribute(e,t){return this._x=e.getX(t),this._y=e.getY(t),this._z=e.getZ(t),this._w=e.getW(t),this._onChangeCallback(),this}toJSON(){return this.toArray()}_onChange(e){return this._onChangeCallback=e,this}_onChangeCallback(){}*[Symbol.iterator](){yield this._x,yield this._y,yield this._z,yield this._w}},P=class i{static{i.prototype.isVector3=!0}constructor(e=0,t=0,n=0){this.x=e,this.y=t,this.z=n}set(e,t,n){return n===void 0&&(n=this.z),this.x=e,this.y=t,this.z=n,this}setScalar(e){return this.x=e,this.y=e,this.z=e,this}setX(e){return this.x=e,this}setY(e){return this.y=e,this}setZ(e){return this.z=e,this}setComponent(e,t){switch(e){case 0:this.x=t;break;case 1:this.y=t;break;case 2:this.z=t;break;default:throw new Error("THREE.Vector3: index is out of range: "+e)}return this}getComponent(e){switch(e){case 0:return this.x;case 1:return this.y;case 2:return this.z;default:throw new Error("THREE.Vector3: index is out of range: "+e)}}clone(){return new this.constructor(this.x,this.y,this.z)}copy(e){return this.x=e.x,this.y=e.y,this.z=e.z,this}add(e){return this.x+=e.x,this.y+=e.y,this.z+=e.z,this}addScalar(e){return this.x+=e,this.y+=e,this.z+=e,this}addVectors(e,t){return this.x=e.x+t.x,this.y=e.y+t.y,this.z=e.z+t.z,this}addScaledVector(e,t){return this.x+=e.x*t,this.y+=e.y*t,this.z+=e.z*t,this}sub(e){return this.x-=e.x,this.y-=e.y,this.z-=e.z,this}subScalar(e){return this.x-=e,this.y-=e,this.z-=e,this}subVectors(e,t){return this.x=e.x-t.x,this.y=e.y-t.y,this.z=e.z-t.z,this}multiply(e){return this.x*=e.x,this.y*=e.y,this.z*=e.z,this}multiplyScalar(e){return this.x*=e,this.y*=e,this.z*=e,this}multiplyVectors(e,t){return this.x=e.x*t.x,this.y=e.y*t.y,this.z=e.z*t.z,this}applyEuler(e){return this.applyQuaternion(qd.setFromEuler(e))}applyAxisAngle(e,t){return this.applyQuaternion(qd.setFromAxisAngle(e,t))}applyMatrix3(e){let t=this.x,n=this.y,s=this.z,r=e.elements;return this.x=r[0]*t+r[3]*n+r[6]*s,this.y=r[1]*t+r[4]*n+r[7]*s,this.z=r[2]*t+r[5]*n+r[8]*s,this}applyNormalMatrix(e){return this.applyMatrix3(e).normalize()}applyMatrix4(e){let t=this.x,n=this.y,s=this.z,r=e.elements,a=1/(r[3]*t+r[7]*n+r[11]*s+r[15]);return this.x=(r[0]*t+r[4]*n+r[8]*s+r[12])*a,this.y=(r[1]*t+r[5]*n+r[9]*s+r[13])*a,this.z=(r[2]*t+r[6]*n+r[10]*s+r[14])*a,this}applyQuaternion(e){let t=this.x,n=this.y,s=this.z,r=e.x,a=e.y,o=e.z,c=e.w,l=2*(a*s-o*n),h=2*(o*t-r*s),u=2*(r*n-a*t);return this.x=t+c*l+a*u-o*h,this.y=n+c*h+o*l-r*u,this.z=s+c*u+r*h-a*l,this}project(e){return this.applyMatrix4(e.matrixWorldInverse).applyMatrix4(e.projectionMatrix)}unproject(e){return this.applyMatrix4(e.projectionMatrixInverse).applyMatrix4(e.matrixWorld)}transformDirection(e){let t=this.x,n=this.y,s=this.z,r=e.elements;return this.x=r[0]*t+r[4]*n+r[8]*s,this.y=r[1]*t+r[5]*n+r[9]*s,this.z=r[2]*t+r[6]*n+r[10]*s,this.normalize()}divide(e){return this.x/=e.x,this.y/=e.y,this.z/=e.z,this}divideScalar(e){return this.multiplyScalar(1/e)}min(e){return this.x=Math.min(this.x,e.x),this.y=Math.min(this.y,e.y),this.z=Math.min(this.z,e.z),this}max(e){return this.x=Math.max(this.x,e.x),this.y=Math.max(this.y,e.y),this.z=Math.max(this.z,e.z),this}clamp(e,t){return this.x=it(this.x,e.x,t.x),this.y=it(this.y,e.y,t.y),this.z=it(this.z,e.z,t.z),this}clampScalar(e,t){return this.x=it(this.x,e,t),this.y=it(this.y,e,t),this.z=it(this.z,e,t),this}clampLength(e,t){let n=this.length();return this.divideScalar(n||1).multiplyScalar(it(n,e,t))}floor(){return this.x=Math.floor(this.x),this.y=Math.floor(this.y),this.z=Math.floor(this.z),this}ceil(){return this.x=Math.ceil(this.x),this.y=Math.ceil(this.y),this.z=Math.ceil(this.z),this}round(){return this.x=Math.round(this.x),this.y=Math.round(this.y),this.z=Math.round(this.z),this}roundToZero(){return this.x=Math.trunc(this.x),this.y=Math.trunc(this.y),this.z=Math.trunc(this.z),this}negate(){return this.x=-this.x,this.y=-this.y,this.z=-this.z,this}dot(e){return this.x*e.x+this.y*e.y+this.z*e.z}lengthSq(){return this.x*this.x+this.y*this.y+this.z*this.z}length(){return Math.sqrt(this.x*this.x+this.y*this.y+this.z*this.z)}manhattanLength(){return Math.abs(this.x)+Math.abs(this.y)+Math.abs(this.z)}normalize(){return this.divideScalar(this.length()||1)}setLength(e){return this.normalize().multiplyScalar(e)}lerp(e,t){return this.x+=(e.x-this.x)*t,this.y+=(e.y-this.y)*t,this.z+=(e.z-this.z)*t,this}lerpVectors(e,t,n){return this.x=e.x+(t.x-e.x)*n,this.y=e.y+(t.y-e.y)*n,this.z=e.z+(t.z-e.z)*n,this}cross(e){return this.crossVectors(this,e)}crossVectors(e,t){let n=e.x,s=e.y,r=e.z,a=t.x,o=t.y,c=t.z;return this.x=s*c-r*o,this.y=r*a-n*c,this.z=n*o-s*a,this}projectOnVector(e){let t=e.lengthSq();if(t===0)return this.set(0,0,0);let n=e.dot(this)/t;return this.copy(e).multiplyScalar(n)}projectOnPlane(e){return nh.copy(this).projectOnVector(e),this.sub(nh)}reflect(e){return this.sub(nh.copy(e).multiplyScalar(2*this.dot(e)))}angleTo(e){let t=Math.sqrt(this.lengthSq()*e.lengthSq());if(t===0)return Math.PI/2;let n=this.dot(e)/t;return Math.acos(it(n,-1,1))}distanceTo(e){return Math.sqrt(this.distanceToSquared(e))}distanceToSquared(e){let t=this.x-e.x,n=this.y-e.y,s=this.z-e.z;return t*t+n*n+s*s}manhattanDistanceTo(e){return Math.abs(this.x-e.x)+Math.abs(this.y-e.y)+Math.abs(this.z-e.z)}setFromSpherical(e){return this.setFromSphericalCoords(e.radius,e.phi,e.theta)}setFromSphericalCoords(e,t,n){let s=Math.sin(t)*e;return this.x=s*Math.sin(n),this.y=Math.cos(t)*e,this.z=s*Math.cos(n),this}setFromCylindrical(e){return this.setFromCylindricalCoords(e.radius,e.theta,e.y)}setFromCylindricalCoords(e,t,n){return this.x=e*Math.sin(t),this.y=n,this.z=e*Math.cos(t),this}setFromMatrixPosition(e){let t=e.elements;return this.x=t[12],this.y=t[13],this.z=t[14],this}setFromMatrixScale(e){let t=this.setFromMatrixColumn(e,0).length(),n=this.setFromMatrixColumn(e,1).length(),s=this.setFromMatrixColumn(e,2).length();return this.x=t,this.y=n,this.z=s,this}setFromMatrixColumn(e,t){return this.fromArray(e.elements,t*4)}setFromMatrix3Column(e,t){return this.fromArray(e.elements,t*3)}setFromEuler(e){return this.x=e._x,this.y=e._y,this.z=e._z,this}setFromColor(e){return this.x=e.r,this.y=e.g,this.z=e.b,this}equals(e){return e.x===this.x&&e.y===this.y&&e.z===this.z}fromArray(e,t=0){return this.x=e[t],this.y=e[t+1],this.z=e[t+2],this}toArray(e=[],t=0){return e[t]=this.x,e[t+1]=this.y,e[t+2]=this.z,e}fromBufferAttribute(e,t){return this.x=e.getX(t),this.y=e.getY(t),this.z=e.getZ(t),this}random(){return this.x=Math.random(),this.y=Math.random(),this.z=Math.random(),this}randomDirection(){let e=Math.random()*Math.PI*2,t=Math.random()*2-1,n=Math.sqrt(1-t*t);return this.x=n*Math.cos(e),this.y=t,this.z=n*Math.sin(e),this}*[Symbol.iterator](){yield this.x,yield this.y,yield this.z}},nh=new P,qd=new ln,et=class i{static{i.prototype.isMatrix3=!0}constructor(e,t,n,s,r,a,o,c,l){this.elements=[1,0,0,0,1,0,0,0,1],e!==void 0&&this.set(e,t,n,s,r,a,o,c,l)}set(e,t,n,s,r,a,o,c,l){let h=this.elements;return h[0]=e,h[1]=s,h[2]=o,h[3]=t,h[4]=r,h[5]=c,h[6]=n,h[7]=a,h[8]=l,this}identity(){return this.set(1,0,0,0,1,0,0,0,1),this}copy(e){let t=this.elements,n=e.elements;return t[0]=n[0],t[1]=n[1],t[2]=n[2],t[3]=n[3],t[4]=n[4],t[5]=n[5],t[6]=n[6],t[7]=n[7],t[8]=n[8],this}extractBasis(e,t,n){return e.setFromMatrix3Column(this,0),t.setFromMatrix3Column(this,1),n.setFromMatrix3Column(this,2),this}setFromMatrix4(e){let t=e.elements;return this.set(t[0],t[4],t[8],t[1],t[5],t[9],t[2],t[6],t[10]),this}multiply(e){return this.multiplyMatrices(this,e)}premultiply(e){return this.multiplyMatrices(e,this)}multiplyMatrices(e,t){let n=e.elements,s=t.elements,r=this.elements,a=n[0],o=n[3],c=n[6],l=n[1],h=n[4],u=n[7],d=n[2],f=n[5],m=n[8],b=s[0],g=s[3],p=s[6],_=s[1],S=s[4],x=s[7],M=s[2],E=s[5],A=s[8];return r[0]=a*b+o*_+c*M,r[3]=a*g+o*S+c*E,r[6]=a*p+o*x+c*A,r[1]=l*b+h*_+u*M,r[4]=l*g+h*S+u*E,r[7]=l*p+h*x+u*A,r[2]=d*b+f*_+m*M,r[5]=d*g+f*S+m*E,r[8]=d*p+f*x+m*A,this}multiplyScalar(e){let t=this.elements;return t[0]*=e,t[3]*=e,t[6]*=e,t[1]*=e,t[4]*=e,t[7]*=e,t[2]*=e,t[5]*=e,t[8]*=e,this}determinant(){let e=this.elements,t=e[0],n=e[1],s=e[2],r=e[3],a=e[4],o=e[5],c=e[6],l=e[7],h=e[8];return t*a*h-t*o*l-n*r*h+n*o*c+s*r*l-s*a*c}invert(){let e=this.elements,t=e[0],n=e[1],s=e[2],r=e[3],a=e[4],o=e[5],c=e[6],l=e[7],h=e[8],u=h*a-o*l,d=o*c-h*r,f=l*r-a*c,m=t*u+n*d+s*f;if(m===0)return this.set(0,0,0,0,0,0,0,0,0);let b=1/m;return e[0]=u*b,e[1]=(s*l-h*n)*b,e[2]=(o*n-s*a)*b,e[3]=d*b,e[4]=(h*t-s*c)*b,e[5]=(s*r-o*t)*b,e[6]=f*b,e[7]=(n*c-l*t)*b,e[8]=(a*t-n*r)*b,this}transpose(){let e,t=this.elements;return e=t[1],t[1]=t[3],t[3]=e,e=t[2],t[2]=t[6],t[6]=e,e=t[5],t[5]=t[7],t[7]=e,this}getNormalMatrix(e){return this.setFromMatrix4(e).invert().transpose()}transposeIntoArray(e){let t=this.elements;return e[0]=t[0],e[1]=t[3],e[2]=t[6],e[3]=t[1],e[4]=t[4],e[5]=t[7],e[6]=t[2],e[7]=t[5],e[8]=t[8],this}setUvTransform(e,t,n,s,r,a,o){let c=Math.cos(r),l=Math.sin(r);return this.set(n*c,n*l,-n*(c*a+l*o)+a+e,-s*l,s*c,-s*(-l*a+c*o)+o+t,0,0,1),this}scale(e,t){return Gs("Matrix3: .scale() is deprecated. Use .makeScale() instead."),this.premultiply(ih.makeScale(e,t)),this}rotate(e){return Gs("Matrix3: .rotate() is deprecated. Use .makeRotation() instead."),this.premultiply(ih.makeRotation(-e)),this}translate(e,t){return Gs("Matrix3: .translate() is deprecated. Use .makeTranslation() instead."),this.premultiply(ih.makeTranslation(e,t)),this}makeTranslation(e,t){return e.isVector2?this.set(1,0,e.x,0,1,e.y,0,0,1):this.set(1,0,e,0,1,t,0,0,1),this}makeRotation(e){let t=Math.cos(e),n=Math.sin(e);return this.set(t,-n,0,n,t,0,0,0,1),this}makeScale(e,t){return this.set(e,0,0,0,t,0,0,0,1),this}equals(e){let t=this.elements,n=e.elements;for(let s=0;s<9;s++)if(t[s]!==n[s])return!1;return!0}fromArray(e,t=0){for(let n=0;n<9;n++)this.elements[n]=e[n+t];return this}toArray(e=[],t=0){let n=this.elements;return e[t]=n[0],e[t+1]=n[1],e[t+2]=n[2],e[t+3]=n[3],e[t+4]=n[4],e[t+5]=n[5],e[t+6]=n[6],e[t+7]=n[7],e[t+8]=n[8],e}clone(){return new this.constructor().fromArray(this.elements)}},ih=new et,Xd=new et().set(.4123908,.3575843,.1804808,.212639,.7151687,.0721923,.0193308,.1191948,.9505322),jd=new et().set(3.2409699,-1.5373832,-.4986108,-.9692436,1.8759675,.0415551,.0556301,-.203977,1.0569715);function pm(){let i={enabled:!0,workingColorSpace:Dn,spaces:{},convert:function(s,r,a){return this.enabled===!1||r===a||!r||!a||(this.spaces[r].transfer===wt&&(s.r=Ji(s.r),s.g=Ji(s.g),s.b=Ji(s.b)),this.spaces[r].primaries!==this.spaces[a].primaries&&(s.applyMatrix3(this.spaces[r].toXYZ),s.applyMatrix3(this.spaces[a].fromXYZ)),this.spaces[a].transfer===wt&&(s.r=wr(s.r),s.g=wr(s.g),s.b=wr(s.b))),s},workingToColorSpace:function(s,r){return this.convert(s,this.workingColorSpace,r)},colorSpaceToWorking:function(s,r){return this.convert(s,r,this.workingColorSpace)},getPrimaries:function(s){return this.spaces[s].primaries},getTransfer:function(s){return s===rs?Ea:this.spaces[s].transfer},getToneMappingMode:function(s){return this.spaces[s].outputColorSpaceConfig.toneMappingMode||"standard"},getLuminanceCoefficients:function(s,r=this.workingColorSpace){return s.fromArray(this.spaces[r].luminanceCoefficients)},define:function(s){Object.assign(this.spaces,s)},_getMatrix:function(s,r,a){return s.copy(this.spaces[r].toXYZ).multiply(this.spaces[a].fromXYZ)},_getDrawingBufferColorSpace:function(s){return this.spaces[s].outputColorSpaceConfig.drawingBufferColorSpace},_getUnpackColorSpace:function(s=this.workingColorSpace){return this.spaces[s].workingColorSpaceConfig.unpackColorSpace},fromWorkingColorSpace:function(s,r){return Gs("ColorManagement: .fromWorkingColorSpace() has been renamed to .workingToColorSpace()."),i.workingToColorSpace(s,r)},toWorkingColorSpace:function(s,r){return Gs("ColorManagement: .toWorkingColorSpace() has been renamed to .colorSpaceToWorking()."),i.colorSpaceToWorking(s,r)}},e=[.64,.33,.3,.6,.15,.06],t=[.2126,.7152,.0722],n=[.3127,.329];return i.define({[Dn]:{primaries:e,whitePoint:n,transfer:Ea,toXYZ:Xd,fromXYZ:jd,luminanceCoefficients:t,workingColorSpaceConfig:{unpackColorSpace:pt},outputColorSpaceConfig:{drawingBufferColorSpace:pt}},[pt]:{primaries:e,whitePoint:n,transfer:wt,toXYZ:Xd,fromXYZ:jd,luminanceCoefficients:t,outputColorSpaceConfig:{drawingBufferColorSpace:pt}}}),i}var lt=pm();function Ji(i){return i<.04045?i*.0773993808:Math.pow(i*.9478672986+.0521327014,2.4)}function wr(i){return i<.0031308?i*12.92:1.055*Math.pow(i,.41666)-.055}var dr,dc=class{static getDataURL(e,t="image/png"){if(/^data:/i.test(e.src)||typeof HTMLCanvasElement>"u")return e.src;let n;if(e instanceof HTMLCanvasElement)n=e;else{dr===void 0&&(dr=Pr("canvas")),dr.width=e.width,dr.height=e.height;let s=dr.getContext("2d");e instanceof ImageData?s.putImageData(e,0,0):s.drawImage(e,0,0,e.width,e.height),n=dr}return n.toDataURL(t)}static sRGBToLinear(e){if(typeof HTMLImageElement<"u"&&e instanceof HTMLImageElement||typeof HTMLCanvasElement<"u"&&e instanceof HTMLCanvasElement||typeof ImageBitmap<"u"&&e instanceof ImageBitmap){let t=Pr("canvas");t.width=e.width,t.height=e.height;let n=t.getContext("2d");n.drawImage(e,0,0,e.width,e.height);let s=n.getImageData(0,0,e.width,e.height),r=s.data;for(let a=0;a<r.length;a++)r[a]=Ji(r[a]/255)*255;return n.putImageData(s,0,0),t}else if(e.data){let t=e.data.slice(0);for(let n=0;n<t.length;n++)t instanceof Uint8Array||t instanceof Uint8ClampedArray?t[n]=Math.floor(Ji(t[n]/255)*255):t[n]=Ji(t[n]);return{data:t,width:e.width,height:e.height}}else return Ve("ImageUtils.sRGBToLinear(): Unsupported image type. No color space conversion applied."),e}},mm=0,Lr=class{constructor(e=null){this.isTextureSource=!0,Object.defineProperty(this,"id",{value:mm++}),this.uuid=ii(),this.data=e,this.dataReady=!0,this.version=0}getSize(e){let t=this.data;return typeof HTMLVideoElement<"u"&&t instanceof HTMLVideoElement?e.set(t.videoWidth,t.videoHeight,0):typeof VideoFrame<"u"&&t instanceof VideoFrame?e.set(t.displayWidth,t.displayHeight,0):t!==null?e.set(t.width,t.height,t.depth||0):e.set(0,0,0),e}set needsUpdate(e){e===!0&&this.version++}toJSON(e){let t=e===void 0||typeof e=="string";if(!t&&e.images[this.uuid]!==void 0)return e.images[this.uuid];let n={uuid:this.uuid,url:""},s=this.data;if(s!==null){let r;if(Array.isArray(s)){r=[];for(let a=0,o=s.length;a<o;a++)s[a].isDataTexture?r.push(sh(s[a].image)):r.push(sh(s[a]))}else r=sh(s);n.url=r}return t||(e.images[this.uuid]=n),n}};function sh(i){return typeof HTMLImageElement<"u"&&i instanceof HTMLImageElement||typeof HTMLCanvasElement<"u"&&i instanceof HTMLCanvasElement||typeof ImageBitmap<"u"&&i instanceof ImageBitmap?dc.getDataURL(i):i.data?{data:Array.from(i.data),width:i.width,height:i.height,type:i.data.constructor.name}:(Ve("Texture: Unable to serialize Texture."),{})}var gm=0,rh=new P,hn=class i extends _i{constructor(e=i.DEFAULT_IMAGE,t=i.DEFAULT_MAPPING,n=ni,s=ni,r=Xt,a=qn,o=jn,c=Un,l=i.DEFAULT_ANISOTROPY,h=rs){super(),this.isTexture=!0,Object.defineProperty(this,"id",{value:gm++}),this.uuid=ii(),this.name="",this.source=new Lr(e),this.mipmaps=[],this.mapping=t,this.channel=0,this.wrapS=n,this.wrapT=s,this.magFilter=r,this.minFilter=a,this.anisotropy=l,this.format=o,this.internalFormat=null,this.type=c,this.offset=new ae(0,0),this.repeat=new ae(1,1),this.center=new ae(0,0),this.rotation=0,this.matrixAutoUpdate=!0,this.matrix=new et,this.generateMipmaps=!0,this.premultiplyAlpha=!1,this.flipY=!0,this.unpackAlignment=4,this.colorSpace=h,this.userData={},this.updateRanges=[],this.version=0,this.onUpdate=null,this.renderTarget=null,this.isRenderTargetTexture=!1,this.isArrayTexture=!!(e&&e.depth&&e.depth>1),this.pmremVersion=0,this.normalized=!1}get width(){return this.source.getSize(rh).x}get height(){return this.source.getSize(rh).y}get depth(){return this.source.getSize(rh).z}get image(){return this.source.data}set image(e){this.source.data=e}updateMatrix(){this.matrix.setUvTransform(this.offset.x,this.offset.y,this.repeat.x,this.repeat.y,this.rotation,this.center.x,this.center.y)}addUpdateRange(e,t){this.updateRanges.push({start:e,count:t})}clearUpdateRanges(){this.updateRanges.length=0}clone(){return new this.constructor().copy(this)}copy(e){return this.name=e.name,this.source=e.source,this.mipmaps=e.mipmaps.slice(0),this.mapping=e.mapping,this.channel=e.channel,this.wrapS=e.wrapS,this.wrapT=e.wrapT,this.magFilter=e.magFilter,this.minFilter=e.minFilter,this.anisotropy=e.anisotropy,this.format=e.format,this.internalFormat=e.internalFormat,this.type=e.type,this.normalized=e.normalized,this.offset.copy(e.offset),this.repeat.copy(e.repeat),this.center.copy(e.center),this.rotation=e.rotation,this.matrixAutoUpdate=e.matrixAutoUpdate,this.matrix.copy(e.matrix),this.generateMipmaps=e.generateMipmaps,this.premultiplyAlpha=e.premultiplyAlpha,this.flipY=e.flipY,this.unpackAlignment=e.unpackAlignment,this.colorSpace=e.colorSpace,this.renderTarget=e.renderTarget,this.isRenderTargetTexture=e.isRenderTargetTexture,this.isArrayTexture=e.isArrayTexture,this.userData=JSON.parse(JSON.stringify(e.userData)),this.needsUpdate=!0,this}setValues(e){for(let t in e){let n=e[t];if(n===void 0){Ve(`Texture.setValues(): parameter '${t}' has value of undefined.`);continue}let s=this[t];if(s===void 0){Ve(`Texture.setValues(): property '${t}' does not exist.`);continue}s&&n&&s.isVector2&&n.isVector2||s&&n&&s.isVector3&&n.isVector3||s&&n&&s.isMatrix3&&n.isMatrix3?s.copy(n):this[t]=n}}toJSON(e){let t=e===void 0||typeof e=="string";if(!t&&e.textures[this.uuid]!==void 0)return e.textures[this.uuid];let n={metadata:{version:4.7,type:"Texture",generator:"Texture.toJSON"},uuid:this.uuid,name:this.name,image:this.source.toJSON(e).uuid,mapping:this.mapping,channel:this.channel,repeat:[this.repeat.x,this.repeat.y],offset:[this.offset.x,this.offset.y],center:[this.center.x,this.center.y],rotation:this.rotation,wrap:[this.wrapS,this.wrapT],format:this.format,internalFormat:this.internalFormat,type:this.type,normalized:this.normalized,colorSpace:this.colorSpace,minFilter:this.minFilter,magFilter:this.magFilter,anisotropy:this.anisotropy,flipY:this.flipY,generateMipmaps:this.generateMipmaps,premultiplyAlpha:this.premultiplyAlpha,unpackAlignment:this.unpackAlignment};return Object.keys(this.userData).length>0&&(n.userData=this.userData),t||(e.textures[this.uuid]=n),n}dispose(){this.dispatchEvent({type:"dispose"})}transformUv(e){if(this.mapping!==iu)return e;if(e.applyMatrix3(this.matrix),e.x<0||e.x>1)switch(this.wrapS){case Pi:e.x=e.x-Math.floor(e.x);break;case ni:e.x=e.x<0?0:1;break;case Rr:Math.abs(Math.floor(e.x)%2)===1?e.x=Math.ceil(e.x)-e.x:e.x=e.x-Math.floor(e.x);break}if(e.y<0||e.y>1)switch(this.wrapT){case Pi:e.y=e.y-Math.floor(e.y);break;case ni:e.y=e.y<0?0:1;break;case Rr:Math.abs(Math.floor(e.y)%2)===1?e.y=Math.ceil(e.y)-e.y:e.y=e.y-Math.floor(e.y);break}return this.flipY&&(e.y=1-e.y),e}set needsUpdate(e){e===!0&&(this.version++,this.source.needsUpdate=!0)}set needsPMREMUpdate(e){e===!0&&this.pmremVersion++}};hn.DEFAULT_IMAGE=null;hn.DEFAULT_MAPPING=iu;hn.DEFAULT_ANISOTROPY=1;var Pt=class i{static{i.prototype.isVector4=!0}constructor(e=0,t=0,n=0,s=1){this.x=e,this.y=t,this.z=n,this.w=s}get width(){return this.z}set width(e){this.z=e}get height(){return this.w}set height(e){this.w=e}set(e,t,n,s){return this.x=e,this.y=t,this.z=n,this.w=s,this}setScalar(e){return this.x=e,this.y=e,this.z=e,this.w=e,this}setX(e){return this.x=e,this}setY(e){return this.y=e,this}setZ(e){return this.z=e,this}setW(e){return this.w=e,this}setComponent(e,t){switch(e){case 0:this.x=t;break;case 1:this.y=t;break;case 2:this.z=t;break;case 3:this.w=t;break;default:throw new Error("THREE.Vector4: index is out of range: "+e)}return this}getComponent(e){switch(e){case 0:return this.x;case 1:return this.y;case 2:return this.z;case 3:return this.w;default:throw new Error("THREE.Vector4: index is out of range: "+e)}}clone(){return new this.constructor(this.x,this.y,this.z,this.w)}copy(e){return this.x=e.x,this.y=e.y,this.z=e.z,this.w=e.w!==void 0?e.w:1,this}add(e){return this.x+=e.x,this.y+=e.y,this.z+=e.z,this.w+=e.w,this}addScalar(e){return this.x+=e,this.y+=e,this.z+=e,this.w+=e,this}addVectors(e,t){return this.x=e.x+t.x,this.y=e.y+t.y,this.z=e.z+t.z,this.w=e.w+t.w,this}addScaledVector(e,t){return this.x+=e.x*t,this.y+=e.y*t,this.z+=e.z*t,this.w+=e.w*t,this}sub(e){return this.x-=e.x,this.y-=e.y,this.z-=e.z,this.w-=e.w,this}subScalar(e){return this.x-=e,this.y-=e,this.z-=e,this.w-=e,this}subVectors(e,t){return this.x=e.x-t.x,this.y=e.y-t.y,this.z=e.z-t.z,this.w=e.w-t.w,this}multiply(e){return this.x*=e.x,this.y*=e.y,this.z*=e.z,this.w*=e.w,this}multiplyScalar(e){return this.x*=e,this.y*=e,this.z*=e,this.w*=e,this}applyMatrix4(e){let t=this.x,n=this.y,s=this.z,r=this.w,a=e.elements;return this.x=a[0]*t+a[4]*n+a[8]*s+a[12]*r,this.y=a[1]*t+a[5]*n+a[9]*s+a[13]*r,this.z=a[2]*t+a[6]*n+a[10]*s+a[14]*r,this.w=a[3]*t+a[7]*n+a[11]*s+a[15]*r,this}divide(e){return this.x/=e.x,this.y/=e.y,this.z/=e.z,this.w/=e.w,this}divideScalar(e){return this.multiplyScalar(1/e)}setAxisAngleFromQuaternion(e){this.w=2*Math.acos(e.w);let t=Math.sqrt(1-e.w*e.w);return t<1e-4?(this.x=1,this.y=0,this.z=0):(this.x=e.x/t,this.y=e.y/t,this.z=e.z/t),this}setAxisAngleFromRotationMatrix(e){let t,n,s,r,c=e.elements,l=c[0],h=c[4],u=c[8],d=c[1],f=c[5],m=c[9],b=c[2],g=c[6],p=c[10];if(Math.abs(h-d)<.01&&Math.abs(u-b)<.01&&Math.abs(m-g)<.01){if(Math.abs(h+d)<.1&&Math.abs(u+b)<.1&&Math.abs(m+g)<.1&&Math.abs(l+f+p-3)<.1)return this.set(1,0,0,0),this;t=Math.PI;let S=(l+1)/2,x=(f+1)/2,M=(p+1)/2,E=(h+d)/4,A=(u+b)/4,y=(m+g)/4;return S>x&&S>M?S<.01?(n=0,s=.707106781,r=.707106781):(n=Math.sqrt(S),s=E/n,r=A/n):x>M?x<.01?(n=.707106781,s=0,r=.707106781):(s=Math.sqrt(x),n=E/s,r=y/s):M<.01?(n=.707106781,s=.707106781,r=0):(r=Math.sqrt(M),n=A/r,s=y/r),this.set(n,s,r,t),this}let _=Math.sqrt((g-m)*(g-m)+(u-b)*(u-b)+(d-h)*(d-h));return Math.abs(_)<.001&&(_=1),this.x=(g-m)/_,this.y=(u-b)/_,this.z=(d-h)/_,this.w=Math.acos((l+f+p-1)/2),this}setFromMatrixPosition(e){let t=e.elements;return this.x=t[12],this.y=t[13],this.z=t[14],this.w=t[15],this}min(e){return this.x=Math.min(this.x,e.x),this.y=Math.min(this.y,e.y),this.z=Math.min(this.z,e.z),this.w=Math.min(this.w,e.w),this}max(e){return this.x=Math.max(this.x,e.x),this.y=Math.max(this.y,e.y),this.z=Math.max(this.z,e.z),this.w=Math.max(this.w,e.w),this}clamp(e,t){return this.x=it(this.x,e.x,t.x),this.y=it(this.y,e.y,t.y),this.z=it(this.z,e.z,t.z),this.w=it(this.w,e.w,t.w),this}clampScalar(e,t){return this.x=it(this.x,e,t),this.y=it(this.y,e,t),this.z=it(this.z,e,t),this.w=it(this.w,e,t),this}clampLength(e,t){let n=this.length();return this.divideScalar(n||1).multiplyScalar(it(n,e,t))}floor(){return this.x=Math.floor(this.x),this.y=Math.floor(this.y),this.z=Math.floor(this.z),this.w=Math.floor(this.w),this}ceil(){return this.x=Math.ceil(this.x),this.y=Math.ceil(this.y),this.z=Math.ceil(this.z),this.w=Math.ceil(this.w),this}round(){return this.x=Math.round(this.x),this.y=Math.round(this.y),this.z=Math.round(this.z),this.w=Math.round(this.w),this}roundToZero(){return this.x=Math.trunc(this.x),this.y=Math.trunc(this.y),this.z=Math.trunc(this.z),this.w=Math.trunc(this.w),this}negate(){return this.x=-this.x,this.y=-this.y,this.z=-this.z,this.w=-this.w,this}dot(e){return this.x*e.x+this.y*e.y+this.z*e.z+this.w*e.w}lengthSq(){return this.x*this.x+this.y*this.y+this.z*this.z+this.w*this.w}length(){return Math.sqrt(this.x*this.x+this.y*this.y+this.z*this.z+this.w*this.w)}manhattanLength(){return Math.abs(this.x)+Math.abs(this.y)+Math.abs(this.z)+Math.abs(this.w)}normalize(){return this.divideScalar(this.length()||1)}setLength(e){return this.normalize().multiplyScalar(e)}lerp(e,t){return this.x+=(e.x-this.x)*t,this.y+=(e.y-this.y)*t,this.z+=(e.z-this.z)*t,this.w+=(e.w-this.w)*t,this}lerpVectors(e,t,n){return this.x=e.x+(t.x-e.x)*n,this.y=e.y+(t.y-e.y)*n,this.z=e.z+(t.z-e.z)*n,this.w=e.w+(t.w-e.w)*n,this}equals(e){return e.x===this.x&&e.y===this.y&&e.z===this.z&&e.w===this.w}fromArray(e,t=0){return this.x=e[t],this.y=e[t+1],this.z=e[t+2],this.w=e[t+3],this}toArray(e=[],t=0){return e[t]=this.x,e[t+1]=this.y,e[t+2]=this.z,e[t+3]=this.w,e}fromBufferAttribute(e,t){return this.x=e.getX(t),this.y=e.getY(t),this.z=e.getZ(t),this.w=e.getW(t),this}random(){return this.x=Math.random(),this.y=Math.random(),this.z=Math.random(),this.w=Math.random(),this}*[Symbol.iterator](){yield this.x,yield this.y,yield this.z,yield this.w}},fc=class extends _i{constructor(e=1,t=1,n={}){super(),n=Object.assign({generateMipmaps:!1,internalFormat:null,minFilter:Xt,depthBuffer:!0,stencilBuffer:!1,resolveColorBuffer:!0,resolveDepthBuffer:!0,resolveStencilBuffer:!0,storeMultisampledColorBuffer:!0,storeMultisampledDepthBuffer:!0,storeMultisampledStencilBuffer:!0,depthTexture:null,samples:0,count:1,depth:1,multiview:!1,useArrayDepthTexture:!1},n),this.isRenderTarget=!0,this.width=e,this.height=t,this.depth=n.depth,this.scissor=new Pt(0,0,e,t),this.scissorTest=!1,this.viewport=new Pt(0,0,e,t),this.textures=[];let s={width:e,height:t,depth:n.depth},r=new hn(s),a=n.count;for(let o=0;o<a;o++)this.textures[o]=r.clone(),this.textures[o].isRenderTargetTexture=!0,this.textures[o].renderTarget=this;this._setTextureOptions(n),this.depthBuffer=n.depthBuffer,this.stencilBuffer=n.stencilBuffer,this.resolveColorBuffer=n.resolveColorBuffer,this.resolveDepthBuffer=n.resolveDepthBuffer,this.resolveStencilBuffer=n.resolveStencilBuffer,this.storeMultisampledColorBuffer=n.storeMultisampledColorBuffer,this.storeMultisampledDepthBuffer=n.storeMultisampledDepthBuffer,this.storeMultisampledStencilBuffer=n.storeMultisampledStencilBuffer,this._depthTexture=null,this.depthTexture=n.depthTexture,this.samples=n.samples,this.multiview=n.multiview,this.useArrayDepthTexture=n.useArrayDepthTexture}_setTextureOptions(e={}){let t={minFilter:Xt,generateMipmaps:!1,flipY:!1,internalFormat:null};e.mapping!==void 0&&(t.mapping=e.mapping),e.wrapS!==void 0&&(t.wrapS=e.wrapS),e.wrapT!==void 0&&(t.wrapT=e.wrapT),e.wrapR!==void 0&&(t.wrapR=e.wrapR),e.magFilter!==void 0&&(t.magFilter=e.magFilter),e.minFilter!==void 0&&(t.minFilter=e.minFilter),e.format!==void 0&&(t.format=e.format),e.type!==void 0&&(t.type=e.type),e.anisotropy!==void 0&&(t.anisotropy=e.anisotropy),e.colorSpace!==void 0&&(t.colorSpace=e.colorSpace),e.flipY!==void 0&&(t.flipY=e.flipY),e.generateMipmaps!==void 0&&(t.generateMipmaps=e.generateMipmaps),e.internalFormat!==void 0&&(t.internalFormat=e.internalFormat);for(let n=0;n<this.textures.length;n++)this.textures[n].setValues(t)}get texture(){return this.textures[0]}set texture(e){this.textures[0]=e}set depthTexture(e){this._depthTexture!==null&&this._depthTexture.renderTarget===this&&(this._depthTexture.renderTarget=null),e!==null&&e.renderTarget===null&&(e.renderTarget=this),this._depthTexture=e}get depthTexture(){return this._depthTexture}setSize(e,t,n=1){if(this.width!==e||this.height!==t||this.depth!==n){this.width=e,this.height=t,this.depth=n;for(let s=0,r=this.textures.length;s<r;s++)this.textures[s].image.width=e,this.textures[s].image.height=t,this.textures[s].image.depth=n,this.textures[s].isData3DTexture!==!0&&(this.textures[s].isArrayTexture=this.textures[s].image.depth>1);this.dispose()}this.viewport.set(0,0,e,t),this.scissor.set(0,0,e,t)}clone(){return new this.constructor().copy(this)}copy(e){this.width=e.width,this.height=e.height,this.depth=e.depth,this.scissor.copy(e.scissor),this.scissorTest=e.scissorTest,this.viewport.copy(e.viewport),this.textures.length=0;for(let t=0,n=e.textures.length;t<n;t++){this.textures[t]=e.textures[t].clone(),this.textures[t].isRenderTargetTexture=!0,this.textures[t].renderTarget=this;let s=Object.assign({},e.textures[t].image);this.textures[t].source=new Lr(s)}if(this.depthBuffer=e.depthBuffer,this.stencilBuffer=e.stencilBuffer,this.resolveColorBuffer=e.resolveColorBuffer,this.resolveDepthBuffer=e.resolveDepthBuffer,this.resolveStencilBuffer=e.resolveStencilBuffer,this.storeMultisampledColorBuffer=e.storeMultisampledColorBuffer,this.storeMultisampledDepthBuffer=e.storeMultisampledDepthBuffer,this.storeMultisampledStencilBuffer=e.storeMultisampledStencilBuffer,e.depthTexture!==null)if(e.depthTexture.renderTarget===e){let t=e.depthTexture.clone();t.renderTarget=null,this.depthTexture=t}else this.depthTexture=e.depthTexture;return this.samples=e.samples,this.multiview=e.multiview,this.useArrayDepthTexture=e.useArrayDepthTexture,this}dispose(){this.dispatchEvent({type:"dispose"})}},wn=class extends fc{constructor(e=1,t=1,n={}){super(e,t,n),this.isWebGLRenderTarget=!0}},wa=class extends hn{constructor(e=null,t=1,n=1,s=1){super(null),this.isDataArrayTexture=!0,this.image={data:e,width:t,height:n,depth:s},this.magFilter=Wt,this.minFilter=Wt,this.wrapR=ni,this.generateMipmaps=!1,this.flipY=!1,this.unpackAlignment=1,this.layerUpdates=new Set}copy(e){return super.copy(e),this.wrapR=e.wrapR,this}addLayerUpdate(e){this.layerUpdates.add(e)}clearLayerUpdates(){this.layerUpdates.clear()}};var pc=class extends hn{constructor(e=null,t=1,n=1,s=1){super(null),this.isData3DTexture=!0,this.image={data:e,width:t,height:n,depth:s},this.magFilter=Wt,this.minFilter=Wt,this.wrapR=ni,this.generateMipmaps=!1,this.flipY=!1,this.unpackAlignment=1}copy(e){return super.copy(e),this.wrapR=e.wrapR,this}};var Ze=class i{static{i.prototype.isMatrix4=!0}constructor(e,t,n,s,r,a,o,c,l,h,u,d,f,m,b,g){this.elements=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],e!==void 0&&this.set(e,t,n,s,r,a,o,c,l,h,u,d,f,m,b,g)}set(e,t,n,s,r,a,o,c,l,h,u,d,f,m,b,g){let p=this.elements;return p[0]=e,p[4]=t,p[8]=n,p[12]=s,p[1]=r,p[5]=a,p[9]=o,p[13]=c,p[2]=l,p[6]=h,p[10]=u,p[14]=d,p[3]=f,p[7]=m,p[11]=b,p[15]=g,this}identity(){return this.set(1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1),this}clone(){return new i().fromArray(this.elements)}copy(e){let t=this.elements,n=e.elements;return t[0]=n[0],t[1]=n[1],t[2]=n[2],t[3]=n[3],t[4]=n[4],t[5]=n[5],t[6]=n[6],t[7]=n[7],t[8]=n[8],t[9]=n[9],t[10]=n[10],t[11]=n[11],t[12]=n[12],t[13]=n[13],t[14]=n[14],t[15]=n[15],this}copyPosition(e){let t=this.elements,n=e.elements;return t[12]=n[12],t[13]=n[13],t[14]=n[14],this}setFromMatrix3(e){let t=e.elements;return this.set(t[0],t[3],t[6],0,t[1],t[4],t[7],0,t[2],t[5],t[8],0,0,0,0,1),this}extractBasis(e,t,n){return this.determinantAffine()===0?(e.set(1,0,0),t.set(0,1,0),n.set(0,0,1),this):(e.setFromMatrixColumn(this,0),t.setFromMatrixColumn(this,1),n.setFromMatrixColumn(this,2),this)}makeBasis(e,t,n){return this.set(e.x,t.x,n.x,0,e.y,t.y,n.y,0,e.z,t.z,n.z,0,0,0,0,1),this}extractRotation(e){if(e.determinantAffine()===0)return this.identity();let t=this.elements,n=e.elements,s=1/fr.setFromMatrixColumn(e,0).length(),r=1/fr.setFromMatrixColumn(e,1).length(),a=1/fr.setFromMatrixColumn(e,2).length();return t[0]=n[0]*s,t[1]=n[1]*s,t[2]=n[2]*s,t[3]=0,t[4]=n[4]*r,t[5]=n[5]*r,t[6]=n[6]*r,t[7]=0,t[8]=n[8]*a,t[9]=n[9]*a,t[10]=n[10]*a,t[11]=0,t[12]=0,t[13]=0,t[14]=0,t[15]=1,this}makeRotationFromEuler(e){let t=this.elements,n=e.x,s=e.y,r=e.z,a=Math.cos(n),o=Math.sin(n),c=Math.cos(s),l=Math.sin(s),h=Math.cos(r),u=Math.sin(r);if(e.order==="XYZ"){let d=a*h,f=a*u,m=o*h,b=o*u;t[0]=c*h,t[4]=-c*u,t[8]=l,t[1]=f+m*l,t[5]=d-b*l,t[9]=-o*c,t[2]=b-d*l,t[6]=m+f*l,t[10]=a*c}else if(e.order==="YXZ"){let d=c*h,f=c*u,m=l*h,b=l*u;t[0]=d+b*o,t[4]=m*o-f,t[8]=a*l,t[1]=a*u,t[5]=a*h,t[9]=-o,t[2]=f*o-m,t[6]=b+d*o,t[10]=a*c}else if(e.order==="ZXY"){let d=c*h,f=c*u,m=l*h,b=l*u;t[0]=d-b*o,t[4]=-a*u,t[8]=m+f*o,t[1]=f+m*o,t[5]=a*h,t[9]=b-d*o,t[2]=-a*l,t[6]=o,t[10]=a*c}else if(e.order==="ZYX"){let d=a*h,f=a*u,m=o*h,b=o*u;t[0]=c*h,t[4]=m*l-f,t[8]=d*l+b,t[1]=c*u,t[5]=b*l+d,t[9]=f*l-m,t[2]=-l,t[6]=o*c,t[10]=a*c}else if(e.order==="YZX"){let d=a*c,f=a*l,m=o*c,b=o*l;t[0]=c*h,t[4]=b-d*u,t[8]=m*u+f,t[1]=u,t[5]=a*h,t[9]=-o*h,t[2]=-l*h,t[6]=f*u+m,t[10]=d-b*u}else if(e.order==="XZY"){let d=a*c,f=a*l,m=o*c,b=o*l;t[0]=c*h,t[4]=-u,t[8]=l*h,t[1]=d*u+b,t[5]=a*h,t[9]=f*u-m,t[2]=m*u-f,t[6]=o*h,t[10]=b*u+d}return t[3]=0,t[7]=0,t[11]=0,t[12]=0,t[13]=0,t[14]=0,t[15]=1,this}makeRotationFromQuaternion(e){return this.compose(bm,e,xm)}lookAt(e,t,n){let s=this.elements;return kn.subVectors(e,t),kn.lengthSq()===0&&(kn.z=1),kn.normalize(),ds.crossVectors(n,kn),ds.lengthSq()===0&&(Math.abs(n.z)===1?kn.x+=1e-4:kn.z+=1e-4,kn.normalize(),ds.crossVectors(n,kn)),ds.normalize(),Co.crossVectors(kn,ds),s[0]=ds.x,s[4]=Co.x,s[8]=kn.x,s[1]=ds.y,s[5]=Co.y,s[9]=kn.y,s[2]=ds.z,s[6]=Co.z,s[10]=kn.z,this}multiply(e){return this.multiplyMatrices(this,e)}premultiply(e){return this.multiplyMatrices(e,this)}multiplyMatrices(e,t){let n=e.elements,s=t.elements,r=this.elements,a=n[0],o=n[4],c=n[8],l=n[12],h=n[1],u=n[5],d=n[9],f=n[13],m=n[2],b=n[6],g=n[10],p=n[14],_=n[3],S=n[7],x=n[11],M=n[15],E=s[0],A=s[4],y=s[8],w=s[12],R=s[1],I=s[5],L=s[9],F=s[13],D=s[2],k=s[6],W=s[10],X=s[14],re=s[3],j=s[7],ee=s[11],te=s[15];return r[0]=a*E+o*R+c*D+l*re,r[4]=a*A+o*I+c*k+l*j,r[8]=a*y+o*L+c*W+l*ee,r[12]=a*w+o*F+c*X+l*te,r[1]=h*E+u*R+d*D+f*re,r[5]=h*A+u*I+d*k+f*j,r[9]=h*y+u*L+d*W+f*ee,r[13]=h*w+u*F+d*X+f*te,r[2]=m*E+b*R+g*D+p*re,r[6]=m*A+b*I+g*k+p*j,r[10]=m*y+b*L+g*W+p*ee,r[14]=m*w+b*F+g*X+p*te,r[3]=_*E+S*R+x*D+M*re,r[7]=_*A+S*I+x*k+M*j,r[11]=_*y+S*L+x*W+M*ee,r[15]=_*w+S*F+x*X+M*te,this}multiplyScalar(e){let t=this.elements;return t[0]*=e,t[4]*=e,t[8]*=e,t[12]*=e,t[1]*=e,t[5]*=e,t[9]*=e,t[13]*=e,t[2]*=e,t[6]*=e,t[10]*=e,t[14]*=e,t[3]*=e,t[7]*=e,t[11]*=e,t[15]*=e,this}determinant(){let e=this.elements,t=e[0],n=e[4],s=e[8],r=e[12],a=e[1],o=e[5],c=e[9],l=e[13],h=e[2],u=e[6],d=e[10],f=e[14],m=e[3],b=e[7],g=e[11],p=e[15],_=c*f-l*d,S=o*f-l*u,x=o*d-c*u,M=a*f-l*h,E=a*d-c*h,A=a*u-o*h;return t*(b*_-g*S+p*x)-n*(m*_-g*M+p*E)+s*(m*S-b*M+p*A)-r*(m*x-b*E+g*A)}determinantAffine(){let e=this.elements,t=e[0],n=e[4],s=e[8],r=e[1],a=e[5],o=e[9],c=e[2],l=e[6],h=e[10];return t*(a*h-o*l)-n*(r*h-o*c)+s*(r*l-a*c)}transpose(){let e=this.elements,t;return t=e[1],e[1]=e[4],e[4]=t,t=e[2],e[2]=e[8],e[8]=t,t=e[6],e[6]=e[9],e[9]=t,t=e[3],e[3]=e[12],e[12]=t,t=e[7],e[7]=e[13],e[13]=t,t=e[11],e[11]=e[14],e[14]=t,this}setPosition(e,t,n){let s=this.elements;return e.isVector3?(s[12]=e.x,s[13]=e.y,s[14]=e.z):(s[12]=e,s[13]=t,s[14]=n),this}invert(){let e=this.elements,t=e[0],n=e[1],s=e[2],r=e[3],a=e[4],o=e[5],c=e[6],l=e[7],h=e[8],u=e[9],d=e[10],f=e[11],m=e[12],b=e[13],g=e[14],p=e[15],_=t*o-n*a,S=t*c-s*a,x=t*l-r*a,M=n*c-s*o,E=n*l-r*o,A=s*l-r*c,y=h*b-u*m,w=h*g-d*m,R=h*p-f*m,I=u*g-d*b,L=u*p-f*b,F=d*p-f*g,D=_*F-S*L+x*I+M*R-E*w+A*y;if(D===0)return this.set(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0);let k=1/D;return e[0]=(o*F-c*L+l*I)*k,e[1]=(s*L-n*F-r*I)*k,e[2]=(b*A-g*E+p*M)*k,e[3]=(d*E-u*A-f*M)*k,e[4]=(c*R-a*F-l*w)*k,e[5]=(t*F-s*R+r*w)*k,e[6]=(g*x-m*A-p*S)*k,e[7]=(h*A-d*x+f*S)*k,e[8]=(a*L-o*R+l*y)*k,e[9]=(n*R-t*L-r*y)*k,e[10]=(m*E-b*x+p*_)*k,e[11]=(u*x-h*E-f*_)*k,e[12]=(o*w-a*I-c*y)*k,e[13]=(t*I-n*w+s*y)*k,e[14]=(b*S-m*M-g*_)*k,e[15]=(h*M-u*S+d*_)*k,this}scale(e){let t=this.elements,n=e.x,s=e.y,r=e.z;return t[0]*=n,t[4]*=s,t[8]*=r,t[1]*=n,t[5]*=s,t[9]*=r,t[2]*=n,t[6]*=s,t[10]*=r,t[3]*=n,t[7]*=s,t[11]*=r,this}getMaxScaleOnAxis(){let e=this.elements,t=e[0]*e[0]+e[1]*e[1]+e[2]*e[2],n=e[4]*e[4]+e[5]*e[5]+e[6]*e[6],s=e[8]*e[8]+e[9]*e[9]+e[10]*e[10];return Math.sqrt(Math.max(t,n,s))}makeTranslation(e,t,n){return e.isVector3?this.set(1,0,0,e.x,0,1,0,e.y,0,0,1,e.z,0,0,0,1):this.set(1,0,0,e,0,1,0,t,0,0,1,n,0,0,0,1),this}makeRotationX(e){let t=Math.cos(e),n=Math.sin(e);return this.set(1,0,0,0,0,t,-n,0,0,n,t,0,0,0,0,1),this}makeRotationY(e){let t=Math.cos(e),n=Math.sin(e);return this.set(t,0,n,0,0,1,0,0,-n,0,t,0,0,0,0,1),this}makeRotationZ(e){let t=Math.cos(e),n=Math.sin(e);return this.set(t,-n,0,0,n,t,0,0,0,0,1,0,0,0,0,1),this}makeRotationAxis(e,t){let n=Math.cos(t),s=Math.sin(t),r=1-n,a=e.x,o=e.y,c=e.z,l=r*a,h=r*o;return this.set(l*a+n,l*o-s*c,l*c+s*o,0,l*o+s*c,h*o+n,h*c-s*a,0,l*c-s*o,h*c+s*a,r*c*c+n,0,0,0,0,1),this}makeScale(e,t,n){return this.set(e,0,0,0,0,t,0,0,0,0,n,0,0,0,0,1),this}makeShear(e,t,n,s,r,a){return this.set(1,n,r,0,e,1,a,0,t,s,1,0,0,0,0,1),this}compose(e,t,n){let s=this.elements,r=t._x,a=t._y,o=t._z,c=t._w,l=r+r,h=a+a,u=o+o,d=r*l,f=r*h,m=r*u,b=a*h,g=a*u,p=o*u,_=c*l,S=c*h,x=c*u,M=n.x,E=n.y,A=n.z;return s[0]=(1-(b+p))*M,s[1]=(f+x)*M,s[2]=(m-S)*M,s[3]=0,s[4]=(f-x)*E,s[5]=(1-(d+p))*E,s[6]=(g+_)*E,s[7]=0,s[8]=(m+S)*A,s[9]=(g-_)*A,s[10]=(1-(d+b))*A,s[11]=0,s[12]=e.x,s[13]=e.y,s[14]=e.z,s[15]=1,this}decompose(e,t,n){let s=this.elements;e.x=s[12],e.y=s[13],e.z=s[14];let r=this.determinantAffine();if(r===0)return n.set(1,1,1),t.identity(),this;let a=fr.set(s[0],s[1],s[2]).length(),o=fr.set(s[4],s[5],s[6]).length(),c=fr.set(s[8],s[9],s[10]).length();r<0&&(a=-a),pi.copy(this);let l=1/a,h=1/o,u=1/c;return pi.elements[0]*=l,pi.elements[1]*=l,pi.elements[2]*=l,pi.elements[4]*=h,pi.elements[5]*=h,pi.elements[6]*=h,pi.elements[8]*=u,pi.elements[9]*=u,pi.elements[10]*=u,t.setFromRotationMatrix(pi),n.x=a,n.y=o,n.z=c,this}makePerspective(e,t,n,s,r,a,o=xi,c=!1){let l=this.elements,h=2*r/(t-e),u=2*r/(n-s),d=(t+e)/(t-e),f=(n+s)/(n-s),m,b;if(c)m=r/(a-r),b=a*r/(a-r);else if(o===xi)m=-(a+r)/(a-r),b=-2*a*r/(a-r);else if(o===Cr)m=-a/(a-r),b=-a*r/(a-r);else throw new Error("THREE.Matrix4.makePerspective(): Invalid coordinate system: "+o);return l[0]=h,l[4]=0,l[8]=d,l[12]=0,l[1]=0,l[5]=u,l[9]=f,l[13]=0,l[2]=0,l[6]=0,l[10]=m,l[14]=b,l[3]=0,l[7]=0,l[11]=-1,l[15]=0,this}makeOrthographic(e,t,n,s,r,a,o=xi,c=!1){let l=this.elements,h=2/(t-e),u=2/(n-s),d=-(t+e)/(t-e),f=-(n+s)/(n-s),m,b;if(c)m=1/(a-r),b=a/(a-r);else if(o===xi)m=-2/(a-r),b=-(a+r)/(a-r);else if(o===Cr)m=-1/(a-r),b=-r/(a-r);else throw new Error("THREE.Matrix4.makeOrthographic(): Invalid coordinate system: "+o);return l[0]=h,l[4]=0,l[8]=0,l[12]=d,l[1]=0,l[5]=u,l[9]=0,l[13]=f,l[2]=0,l[6]=0,l[10]=m,l[14]=b,l[3]=0,l[7]=0,l[11]=0,l[15]=1,this}equals(e){let t=this.elements,n=e.elements;for(let s=0;s<16;s++)if(t[s]!==n[s])return!1;return!0}fromArray(e,t=0){for(let n=0;n<16;n++)this.elements[n]=e[n+t];return this}toArray(e=[],t=0){let n=this.elements;return e[t]=n[0],e[t+1]=n[1],e[t+2]=n[2],e[t+3]=n[3],e[t+4]=n[4],e[t+5]=n[5],e[t+6]=n[6],e[t+7]=n[7],e[t+8]=n[8],e[t+9]=n[9],e[t+10]=n[10],e[t+11]=n[11],e[t+12]=n[12],e[t+13]=n[13],e[t+14]=n[14],e[t+15]=n[15],e}},fr=new P,pi=new Ze,bm=new P(0,0,0),xm=new P(1,1,1),ds=new P,Co=new P,kn=new P,Kd=new Ze,Yd=new ln,si=class i{constructor(e=0,t=0,n=0,s=i.DEFAULT_ORDER){this.isEuler=!0,this._x=e,this._y=t,this._z=n,this._order=s}get x(){return this._x}set x(e){this._x=e,this._onChangeCallback()}get y(){return this._y}set y(e){this._y=e,this._onChangeCallback()}get z(){return this._z}set z(e){this._z=e,this._onChangeCallback()}get order(){return this._order}set order(e){this._order=e,this._onChangeCallback()}set(e,t,n,s=this._order){return this._x=e,this._y=t,this._z=n,this._order=s,this._onChangeCallback(),this}clone(){return new this.constructor(this._x,this._y,this._z,this._order)}copy(e){return this._x=e._x,this._y=e._y,this._z=e._z,this._order=e._order,this._onChangeCallback(),this}setFromRotationMatrix(e,t=this._order,n=!0){let s=e.elements,r=s[0],a=s[4],o=s[8],c=s[1],l=s[5],h=s[9],u=s[2],d=s[6],f=s[10];switch(t){case"XYZ":this._y=Math.asin(it(o,-1,1)),Math.abs(o)<.9999999?(this._x=Math.atan2(-h,f),this._z=Math.atan2(-a,r)):(this._x=Math.atan2(d,l),this._z=0);break;case"YXZ":this._x=Math.asin(-it(h,-1,1)),Math.abs(h)<.9999999?(this._y=Math.atan2(o,f),this._z=Math.atan2(c,l)):(this._y=Math.atan2(-u,r),this._z=0);break;case"ZXY":this._x=Math.asin(it(d,-1,1)),Math.abs(d)<.9999999?(this._y=Math.atan2(-u,f),this._z=Math.atan2(-a,l)):(this._y=0,this._z=Math.atan2(c,r));break;case"ZYX":this._y=Math.asin(-it(u,-1,1)),Math.abs(u)<.9999999?(this._x=Math.atan2(d,f),this._z=Math.atan2(c,r)):(this._x=0,this._z=Math.atan2(-a,l));break;case"YZX":this._z=Math.asin(it(c,-1,1)),Math.abs(c)<.9999999?(this._x=Math.atan2(-h,l),this._y=Math.atan2(-u,r)):(this._x=0,this._y=Math.atan2(o,f));break;case"XZY":this._z=Math.asin(-it(a,-1,1)),Math.abs(a)<.9999999?(this._x=Math.atan2(d,l),this._y=Math.atan2(o,r)):(this._x=Math.atan2(-h,f),this._y=0);break;default:Ve("Euler: .setFromRotationMatrix() encountered an unknown order: "+t)}return this._order=t,n===!0&&this._onChangeCallback(),this}setFromQuaternion(e,t,n){return Kd.makeRotationFromQuaternion(e),this.setFromRotationMatrix(Kd,t,n)}setFromVector3(e,t=this._order){return this.set(e.x,e.y,e.z,t)}reorder(e){return Yd.setFromEuler(this),this.setFromQuaternion(Yd,e)}equals(e){return e._x===this._x&&e._y===this._y&&e._z===this._z&&e._order===this._order}fromArray(e){return this._x=e[0],this._y=e[1],this._z=e[2],e[3]!==void 0&&(this._order=e[3]),this._onChangeCallback(),this}toArray(e=[],t=0){return e[t]=this._x,e[t+1]=this._y,e[t+2]=this._z,e[t+3]=this._order,e}_onChange(e){return this._onChangeCallback=e,this}_onChangeCallback(){}*[Symbol.iterator](){yield this._x,yield this._y,yield this._z,yield this._order}};si.DEFAULT_ORDER="XYZ";var Dr=class{constructor(){this.mask=1}set(e){this.mask=(1<<e|0)>>>0}enable(e){this.mask|=1<<e|0}enableAll(){this.mask=-1}toggle(e){this.mask^=1<<e|0}disable(e){this.mask&=~(1<<e|0)}disableAll(){this.mask=0}test(e){return(this.mask&e.mask)!==0}isEnabled(e){return(this.mask&(1<<e|0))!==0}},_m=0,Jd=new P,pr=new ln,Wi=new Ze,Po=new P,ua=new P,ym=new P,vm=new ln,Zd=new P(1,0,0),$d=new P(0,1,0),Qd=new P(0,0,1),ef={type:"added"},Mm={type:"removed"},mr={type:"childadded",child:null},ah={type:"childremoved",child:null},At=class i extends _i{constructor(){super(),this.isObject3D=!0,Object.defineProperty(this,"id",{value:_m++}),this.uuid=ii(),this.name="",this.type="Object3D",this.parent=null,this.children=[],this.up=i.DEFAULT_UP.clone();let e=new P,t=new si,n=new ln,s=new P(1,1,1);function r(){n.setFromEuler(t,!1)}function a(){t.setFromQuaternion(n,void 0,!1)}t._onChange(r),n._onChange(a),Object.defineProperties(this,{position:{configurable:!0,enumerable:!0,value:e},rotation:{configurable:!0,enumerable:!0,value:t},quaternion:{configurable:!0,enumerable:!0,value:n},scale:{configurable:!0,enumerable:!0,value:s},modelViewMatrix:{value:new Ze},normalMatrix:{value:new et}}),this.matrix=new Ze,this.matrixWorld=new Ze,this.matrixAutoUpdate=i.DEFAULT_MATRIX_AUTO_UPDATE,this.matrixWorldAutoUpdate=i.DEFAULT_MATRIX_WORLD_AUTO_UPDATE,this.matrixWorldNeedsUpdate=!1,this.layers=new Dr,this.visible=!0,this.castShadow=!1,this.receiveShadow=!1,this.frustumCulled=!0,this.renderOrder=0,this.animations=[],this.customDepthMaterial=void 0,this.customDistanceMaterial=void 0,this.static=!1,this.userData={},this.pivot=null}onBeforeShadow(){}onAfterShadow(){}onBeforeRender(){}onAfterRender(){}applyMatrix4(e){this.matrixAutoUpdate&&this.updateMatrix(),this.matrix.premultiply(e),this.matrix.decompose(this.position,this.quaternion,this.scale)}applyQuaternion(e){return this.quaternion.premultiply(e),this}setRotationFromAxisAngle(e,t){this.quaternion.setFromAxisAngle(e,t)}setRotationFromEuler(e){this.quaternion.setFromEuler(e,!0)}setRotationFromMatrix(e){this.quaternion.setFromRotationMatrix(e)}setRotationFromQuaternion(e){this.quaternion.copy(e)}rotateOnAxis(e,t){return pr.setFromAxisAngle(e,t),this.quaternion.multiply(pr),this}rotateOnWorldAxis(e,t){return pr.setFromAxisAngle(e,t),this.quaternion.premultiply(pr),this}rotateX(e){return this.rotateOnAxis(Zd,e)}rotateY(e){return this.rotateOnAxis($d,e)}rotateZ(e){return this.rotateOnAxis(Qd,e)}translateOnAxis(e,t){return Jd.copy(e).applyQuaternion(this.quaternion),this.position.add(Jd.multiplyScalar(t)),this}translateX(e){return this.translateOnAxis(Zd,e)}translateY(e){return this.translateOnAxis($d,e)}translateZ(e){return this.translateOnAxis(Qd,e)}localToWorld(e){return this.updateWorldMatrix(!0,!1),e.applyMatrix4(this.matrixWorld)}worldToLocal(e){return this.updateWorldMatrix(!0,!1),e.applyMatrix4(Wi.copy(this.matrixWorld).invert())}lookAt(e,t,n){e.isVector3?Po.copy(e):Po.set(e,t,n);let s=this.parent;this.updateWorldMatrix(!0,!1),ua.setFromMatrixPosition(this.matrixWorld),this.isCamera||this.isLight?Wi.lookAt(ua,Po,this.up):Wi.lookAt(Po,ua,this.up),this.quaternion.setFromRotationMatrix(Wi),s&&(Wi.extractRotation(s.matrixWorld),pr.setFromRotationMatrix(Wi),this.quaternion.premultiply(pr.invert()))}add(e){if(arguments.length>1){for(let t=0;t<arguments.length;t++)this.add(arguments[t]);return this}return e===this?(Ye("Object3D.add: object can't be added as a child of itself.",e),this):(e&&e.isObject3D?(e.removeFromParent(),e.parent=this,this.children.push(e),e.dispatchEvent(ef),mr.child=e,this.dispatchEvent(mr),mr.child=null):Ye("Object3D.add: object not an instance of THREE.Object3D.",e),this)}remove(e){if(arguments.length>1){for(let n=0;n<arguments.length;n++)this.remove(arguments[n]);return this}let t=this.children.indexOf(e);return t!==-1&&(e.parent=null,this.children.splice(t,1),e.dispatchEvent(Mm),ah.child=e,this.dispatchEvent(ah),ah.child=null),this}removeFromParent(){let e=this.parent;return e!==null&&e.remove(this),this}clear(){return this.remove(...this.children)}attach(e){return this.updateWorldMatrix(!0,!1),Wi.copy(this.matrixWorld).invert(),e.parent!==null&&(e.parent.updateWorldMatrix(!0,!1),Wi.multiply(e.parent.matrixWorld)),e.applyMatrix4(Wi),e.removeFromParent(),e.parent=this,this.children.push(e),e.updateWorldMatrix(!1,!0),e.dispatchEvent(ef),mr.child=e,this.dispatchEvent(mr),mr.child=null,this}getObjectById(e){return this.getObjectByProperty("id",e)}getObjectByName(e){return this.getObjectByProperty("name",e)}getObjectByProperty(e,t){if(this[e]===t)return this;for(let n=0,s=this.children.length;n<s;n++){let a=this.children[n].getObjectByProperty(e,t);if(a!==void 0)return a}}getObjectsByProperty(e,t,n=[]){this[e]===t&&n.push(this);let s=this.children;for(let r=0,a=s.length;r<a;r++)s[r].getObjectsByProperty(e,t,n);return n}getWorldPosition(e){return this.updateWorldMatrix(!0,!1),e.setFromMatrixPosition(this.matrixWorld)}getWorldQuaternion(e){return this.updateWorldMatrix(!0,!1),this.matrixWorld.decompose(ua,e,ym),e}getWorldScale(e){return this.updateWorldMatrix(!0,!1),this.matrixWorld.decompose(ua,vm,e),e}getWorldDirection(e){this.updateWorldMatrix(!0,!1);let t=this.matrixWorld.elements;return e.set(t[8],t[9],t[10]).normalize()}raycast(){}intersectsFrustum(){}traverse(e){e(this);let t=this.children;for(let n=0,s=t.length;n<s;n++)t[n].traverse(e)}traverseVisible(e){if(this.visible===!1)return;e(this);let t=this.children;for(let n=0,s=t.length;n<s;n++)t[n].traverseVisible(e)}traverseAncestors(e){let t=this.parent;t!==null&&(e(t),t.traverseAncestors(e))}updateMatrix(){this.matrix.compose(this.position,this.quaternion,this.scale);let e=this.pivot;if(e!==null){let t=e.x,n=e.y,s=e.z,r=this.matrix.elements;r[12]+=t-r[0]*t-r[4]*n-r[8]*s,r[13]+=n-r[1]*t-r[5]*n-r[9]*s,r[14]+=s-r[2]*t-r[6]*n-r[10]*s}this.matrixWorldNeedsUpdate=!0}updateMatrixWorld(e){this.matrixAutoUpdate&&this.updateMatrix(),(this.matrixWorldNeedsUpdate||e)&&(this.matrixWorldAutoUpdate===!0&&(this.parent===null?this.matrixWorld.copy(this.matrix):this.matrixWorld.multiplyMatrices(this.parent.matrixWorld,this.matrix)),this.matrixWorldNeedsUpdate=!1,e=!0);let t=this.children;for(let n=0,s=t.length;n<s;n++)t[n].updateMatrixWorld(e)}updateWorldMatrix(e,t,n=!1){let s=this.parent;if(e===!0&&s!==null&&s.updateWorldMatrix(!0,!1),this.matrixAutoUpdate&&this.updateMatrix(),(this.matrixWorldNeedsUpdate||n)&&(this.matrixWorldAutoUpdate===!0&&(this.parent===null?this.matrixWorld.copy(this.matrix):this.matrixWorld.multiplyMatrices(this.parent.matrixWorld,this.matrix)),this.matrixWorldNeedsUpdate=!1,n=!0),t===!0){let r=this.children;for(let a=0,o=r.length;a<o;a++)r[a].updateWorldMatrix(!1,!0,n)}}toJSON(e){let t=e===void 0||typeof e=="string",n={};t&&(e={geometries:{},materials:{},textures:{},images:{},shapes:{},skeletons:{},animations:{},nodes:{}},n.metadata={version:4.7,type:"Object",generator:"Object3D.toJSON"});let s={};s.uuid=this.uuid,s.type=this.type,s.name=this.name,s.castShadow=this.castShadow,s.receiveShadow=this.receiveShadow,s.visible=this.visible,s.frustumCulled=this.frustumCulled,s.renderOrder=this.renderOrder,s.static=this.static,s.matrixAutoUpdate=this.matrixAutoUpdate,Object.keys(this.userData).length>0&&(s.userData=this.userData),s.layers=this.layers.mask,s.matrix=this.matrix.toArray(),s.up=this.up.toArray(),this.pivot!==null&&(s.pivot=this.pivot.toArray()),this.morphTargetDictionary!==void 0&&(s.morphTargetDictionary=Object.assign({},this.morphTargetDictionary)),this.morphTargetInfluences!==void 0&&(s.morphTargetInfluences=this.morphTargetInfluences.slice()),this.isInstancedMesh&&(s.type="InstancedMesh",s.count=this.count,s.instanceMatrix=this.instanceMatrix.toJSON(),this.instanceColor!==null&&(s.instanceColor=this.instanceColor.toJSON())),this.isBatchedMesh&&(s.type="BatchedMesh",s.perObjectFrustumCulled=this.perObjectFrustumCulled,s.sortObjects=this.sortObjects,s.drawRanges=this._drawRanges,s.reservedRanges=this._reservedRanges,s.geometryInfo=this._geometryInfo.map(o=>({...o,boundingBox:o.boundingBox?o.boundingBox.toJSON():void 0,boundingSphere:o.boundingSphere?o.boundingSphere.toJSON():void 0})),s.instanceInfo=this._instanceInfo.map(o=>({...o})),s.availableInstanceIds=this._availableInstanceIds.slice(),s.availableGeometryIds=this._availableGeometryIds.slice(),s.nextIndexStart=this._nextIndexStart,s.nextVertexStart=this._nextVertexStart,s.geometryCount=this._geometryCount,s.maxInstanceCount=this._maxInstanceCount,s.maxVertexCount=this._maxVertexCount,s.maxIndexCount=this._maxIndexCount,s.geometryInitialized=this._geometryInitialized,s.matricesTexture=this._matricesTexture.toJSON(e),s.indirectTexture=this._indirectTexture.toJSON(e),this._colorsTexture!==null&&(s.colorsTexture=this._colorsTexture.toJSON(e)),this.boundingSphere!==null&&(s.boundingSphere=this.boundingSphere.toJSON()),this.boundingBox!==null&&(s.boundingBox=this.boundingBox.toJSON()));function r(o,c){return o[c.uuid]===void 0&&(o[c.uuid]=c.toJSON(e)),c.uuid}if(this.isScene)this.background&&(this.background.isColor?s.background=this.background.toJSON():this.background.isTexture&&(s.background=this.background.toJSON(e).uuid)),this.environment&&this.environment.isTexture&&this.environment.isRenderTargetTexture!==!0&&(s.environment=this.environment.toJSON(e).uuid);else if(this.isMesh||this.isLine||this.isPoints){s.geometry=r(e.geometries,this.geometry);let o=this.geometry.parameters;if(o!==void 0&&o.shapes!==void 0){let c=o.shapes;if(Array.isArray(c))for(let l=0,h=c.length;l<h;l++){let u=c[l];r(e.shapes,u)}else r(e.shapes,c)}}if(this.isSkinnedMesh&&(s.bindMode=this.bindMode,s.bindMatrix=this.bindMatrix.toArray(),this.skeleton!==void 0&&(r(e.skeletons,this.skeleton),s.skeleton=this.skeleton.uuid)),this.material!==void 0)if(Array.isArray(this.material)){let o=[];for(let c=0,l=this.material.length;c<l;c++)o.push(r(e.materials,this.material[c]));s.material=o}else s.material=r(e.materials,this.material);if(this.children.length>0){s.children=[];for(let o=0;o<this.children.length;o++)s.children.push(this.children[o].toJSON(e).object)}if(this.animations.length>0){s.animations=[];for(let o=0;o<this.animations.length;o++){let c=this.animations[o];s.animations.push(r(e.animations,c))}}if(t){let o=a(e.geometries),c=a(e.materials),l=a(e.textures),h=a(e.images),u=a(e.shapes),d=a(e.skeletons),f=a(e.animations),m=a(e.nodes);o.length>0&&(n.geometries=o),c.length>0&&(n.materials=c),l.length>0&&(n.textures=l),h.length>0&&(n.images=h),u.length>0&&(n.shapes=u),d.length>0&&(n.skeletons=d),f.length>0&&(n.animations=f),m.length>0&&(n.nodes=m)}return n.object=s,n;function a(o){let c=[];for(let l in o){let h=o[l];delete h.metadata,c.push(h)}return c}}clone(e){return new this.constructor().copy(this,e)}copy(e,t=!0){if(this.name=e.name,this.up.copy(e.up),this.position.copy(e.position),this.rotation.order=e.rotation.order,this.quaternion.copy(e.quaternion),this.scale.copy(e.scale),this.pivot=e.pivot!==null?e.pivot.clone():null,this.matrix.copy(e.matrix),this.matrixWorld.copy(e.matrixWorld),this.matrixAutoUpdate=e.matrixAutoUpdate,this.matrixWorldAutoUpdate=e.matrixWorldAutoUpdate,this.matrixWorldNeedsUpdate=e.matrixWorldNeedsUpdate,this.layers.mask=e.layers.mask,this.visible=e.visible,this.castShadow=e.castShadow,this.receiveShadow=e.receiveShadow,this.frustumCulled=e.frustumCulled,this.renderOrder=e.renderOrder,this.static=e.static,this.animations=e.animations.slice(),this.userData=JSON.parse(JSON.stringify(e.userData)),t===!0)for(let n=0;n<e.children.length;n++){let s=e.children[n];this.add(s.clone())}return this}dispose(){this.dispatchEvent({type:"dispose"})}};At.DEFAULT_UP=new P(0,1,0);At.DEFAULT_MATRIX_AUTO_UPDATE=!0;At.DEFAULT_MATRIX_WORLD_AUTO_UPDATE=!0;var at=class extends At{constructor(){super(),this.isGroup=!0,this.type="Group"}},Sm={type:"move"},Nr=class{constructor(){this._targetRay=null,this._grip=null,this._hand=null}getHandSpace(){return this._hand===null&&(this._hand=new at,this._hand.matrixAutoUpdate=!1,this._hand.visible=!1,this._hand.joints={},this._hand.inputState={pinching:!1}),this._hand}getTargetRaySpace(){return this._targetRay===null&&(this._targetRay=new at,this._targetRay.matrixAutoUpdate=!1,this._targetRay.visible=!1,this._targetRay.hasLinearVelocity=!1,this._targetRay.linearVelocity=new P,this._targetRay.hasAngularVelocity=!1,this._targetRay.angularVelocity=new P),this._targetRay}getGripSpace(){return this._grip===null&&(this._grip=new at,this._grip.matrixAutoUpdate=!1,this._grip.visible=!1,this._grip.hasLinearVelocity=!1,this._grip.linearVelocity=new P,this._grip.hasAngularVelocity=!1,this._grip.angularVelocity=new P,this._grip.eventsEnabled=!1),this._grip}dispatchEvent(e){return this._targetRay!==null&&this._targetRay.dispatchEvent(e),this._grip!==null&&this._grip.dispatchEvent(e),this._hand!==null&&this._hand.dispatchEvent(e),this}connect(e){if(e&&e.hand){let t=this._hand;if(t)for(let n of e.hand.values())this._getHandJoint(t,n)}return this.dispatchEvent({type:"connected",data:e}),this}disconnect(e){return this.dispatchEvent({type:"disconnected",data:e}),this._targetRay!==null&&(this._targetRay.visible=!1),this._grip!==null&&(this._grip.visible=!1),this._hand!==null&&(this._hand.visible=!1),this}update(e,t,n){let s=null,r=null,a=null,o=this._targetRay,c=this._grip,l=this._hand;if(e&&t.session.visibilityState!=="visible-blurred"){if(l&&e.hand){a=!0;for(let b of e.hand.values()){let g=t.getJointPose(b,n),p=this._getHandJoint(l,b);g!==null&&(p.matrix.fromArray(g.transform.matrix),p.matrix.decompose(p.position,p.rotation,p.scale),p.matrixWorldNeedsUpdate=!0,p.jointRadius=g.radius),p.visible=g!==null}let h=l.joints["index-finger-tip"],u=l.joints["thumb-tip"],d=h.position.distanceTo(u.position),f=.02,m=.005;l.inputState.pinching&&d>f+m?(l.inputState.pinching=!1,this.dispatchEvent({type:"pinchend",handedness:e.handedness,target:this})):!l.inputState.pinching&&d<=f-m&&(l.inputState.pinching=!0,this.dispatchEvent({type:"pinchstart",handedness:e.handedness,target:this}))}else c!==null&&e.gripSpace&&(r=t.getPose(e.gripSpace,n),r!==null&&(c.matrix.fromArray(r.transform.matrix),c.matrix.decompose(c.position,c.rotation,c.scale),c.matrixWorldNeedsUpdate=!0,r.linearVelocity?(c.hasLinearVelocity=!0,c.linearVelocity.copy(r.linearVelocity)):c.hasLinearVelocity=!1,r.angularVelocity?(c.hasAngularVelocity=!0,c.angularVelocity.copy(r.angularVelocity)):c.hasAngularVelocity=!1,c.eventsEnabled&&c.dispatchEvent({type:"gripUpdated",data:e,target:this})));o!==null&&(s=t.getPose(e.targetRaySpace,n),s===null&&r!==null&&(s=r),s!==null&&(o.matrix.fromArray(s.transform.matrix),o.matrix.decompose(o.position,o.rotation,o.scale),o.matrixWorldNeedsUpdate=!0,s.linearVelocity?(o.hasLinearVelocity=!0,o.linearVelocity.copy(s.linearVelocity)):o.hasLinearVelocity=!1,s.angularVelocity?(o.hasAngularVelocity=!0,o.angularVelocity.copy(s.angularVelocity)):o.hasAngularVelocity=!1,this.dispatchEvent(Sm)))}return o!==null&&(o.visible=s!==null),c!==null&&(c.visible=r!==null),l!==null&&(l.visible=a!==null),this}_getHandJoint(e,t){if(e.joints[t.jointName]===void 0){let n=new at;n.matrixAutoUpdate=!1,n.visible=!1,e.joints[t.jointName]=n,e.add(n)}return e.joints[t.jointName]}},xp={aliceblue:15792383,antiquewhite:16444375,aqua:65535,aquamarine:8388564,azure:15794175,beige:16119260,bisque:16770244,black:0,blanchedalmond:16772045,blue:255,blueviolet:9055202,brown:10824234,burlywood:14596231,cadetblue:6266528,chartreuse:8388352,chocolate:13789470,coral:16744272,cornflowerblue:6591981,cornsilk:16775388,crimson:14423100,cyan:65535,darkblue:139,darkcyan:35723,darkgoldenrod:12092939,darkgray:11119017,darkgreen:25600,darkgrey:11119017,darkkhaki:12433259,darkmagenta:9109643,darkolivegreen:5597999,darkorange:16747520,darkorchid:10040012,darkred:9109504,darksalmon:15308410,darkseagreen:9419919,darkslateblue:4734347,darkslategray:3100495,darkslategrey:3100495,darkturquoise:52945,darkviolet:9699539,deeppink:16716947,deepskyblue:49151,dimgray:6908265,dimgrey:6908265,dodgerblue:2003199,firebrick:11674146,floralwhite:16775920,forestgreen:2263842,fuchsia:16711935,gainsboro:14474460,ghostwhite:16316671,gold:16766720,goldenrod:14329120,gray:8421504,green:32768,greenyellow:11403055,grey:8421504,honeydew:15794160,hotpink:16738740,indianred:13458524,indigo:4915330,ivory:16777200,khaki:15787660,lavender:15132410,lavenderblush:16773365,lawngreen:8190976,lemonchiffon:16775885,lightblue:11393254,lightcoral:15761536,lightcyan:14745599,lightgoldenrodyellow:16448210,lightgray:13882323,lightgreen:9498256,lightgrey:13882323,lightpink:16758465,lightsalmon:16752762,lightseagreen:2142890,lightskyblue:8900346,lightslategray:7833753,lightslategrey:7833753,lightsteelblue:11584734,lightyellow:16777184,lime:65280,limegreen:3329330,linen:16445670,magenta:16711935,maroon:8388608,mediumaquamarine:6737322,mediumblue:205,mediumorchid:12211667,mediumpurple:9662683,mediumseagreen:3978097,mediumslateblue:8087790,mediumspringgreen:64154,mediumturquoise:4772300,mediumvioletred:13047173,midnightblue:1644912,mintcream:16121850,mistyrose:16770273,moccasin:16770229,navajowhite:16768685,navy:128,oldlace:16643558,olive:8421376,olivedrab:7048739,orange:16753920,orangered:16729344,orchid:14315734,palegoldenrod:15657130,palegreen:10025880,paleturquoise:11529966,palevioletred:14381203,papayawhip:16773077,peachpuff:16767673,peru:13468991,pink:16761035,plum:14524637,powderblue:11591910,purple:8388736,rebeccapurple:6697881,red:16711680,rosybrown:12357519,royalblue:4286945,saddlebrown:9127187,salmon:16416882,sandybrown:16032864,seagreen:3050327,seashell:16774638,sienna:10506797,silver:12632256,skyblue:8900331,slateblue:6970061,slategray:7372944,slategrey:7372944,snow:16775930,springgreen:65407,steelblue:4620980,tan:13808780,teal:32896,thistle:14204888,tomato:16737095,turquoise:4251856,violet:15631086,wheat:16113331,white:16777215,whitesmoke:16119285,yellow:16776960,yellowgreen:10145074},fs={h:0,s:0,l:0},Io={h:0,s:0,l:0};function oh(i,e,t){return t<0&&(t+=1),t>1&&(t-=1),t<1/6?i+(e-i)*6*t:t<1/2?e:t<2/3?i+(e-i)*6*(2/3-t):i}var Be=class{constructor(e,t,n){return this.isColor=!0,this.r=1,this.g=1,this.b=1,this.set(e,t,n)}set(e,t,n){if(t===void 0&&n===void 0){let s=e;s&&s.isColor?this.copy(s):typeof s=="number"?this.setHex(s):typeof s=="string"&&this.setStyle(s)}else this.setRGB(e,t,n);return this}setScalar(e){return this.r=e,this.g=e,this.b=e,this}setHex(e,t=pt){return e=Math.floor(e),this.r=(e>>16&255)/255,this.g=(e>>8&255)/255,this.b=(e&255)/255,lt.colorSpaceToWorking(this,t),this}setRGB(e,t,n,s=lt.workingColorSpace){return this.r=e,this.g=t,this.b=n,lt.colorSpaceToWorking(this,s),this}setHSL(e,t,n,s=lt.workingColorSpace){if(e=fu(e,1),t=it(t,0,1),n=it(n,0,1),t===0)this.r=this.g=this.b=n;else{let r=n<=.5?n*(1+t):n+t-n*t,a=2*n-r;this.r=oh(a,r,e+1/3),this.g=oh(a,r,e),this.b=oh(a,r,e-1/3)}return lt.colorSpaceToWorking(this,s),this}setStyle(e,t=pt){function n(r){r!==void 0&&parseFloat(r)<1&&Ve("Color: Alpha component of "+e+" will be ignored.")}let s;if(s=/^(\w+)\(([^\)]*)\)/.exec(e)){let r,a=s[1],o=s[2];switch(a){case"rgb":case"rgba":if(r=/^\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*(\d*\.?\d+)\s*)?$/.exec(o))return n(r[4]),this.setRGB(Math.min(255,parseInt(r[1],10))/255,Math.min(255,parseInt(r[2],10))/255,Math.min(255,parseInt(r[3],10))/255,t);if(r=/^\s*(\d+)\%\s*,\s*(\d+)\%\s*,\s*(\d+)\%\s*(?:,\s*(\d*\.?\d+)\s*)?$/.exec(o))return n(r[4]),this.setRGB(Math.min(100,parseInt(r[1],10))/100,Math.min(100,parseInt(r[2],10))/100,Math.min(100,parseInt(r[3],10))/100,t);break;case"hsl":case"hsla":if(r=/^\s*(\d*\.?\d+)\s*,\s*(\d*\.?\d+)\%\s*,\s*(\d*\.?\d+)\%\s*(?:,\s*(\d*\.?\d+)\s*)?$/.exec(o))return n(r[4]),this.setHSL(parseFloat(r[1])/360,parseFloat(r[2])/100,parseFloat(r[3])/100,t);break;default:Ve("Color: Unknown color model "+e)}}else if(s=/^\#([A-Fa-f\d]+)$/.exec(e)){let r=s[1],a=r.length;if(a===3)return this.setRGB(parseInt(r.charAt(0),16)/15,parseInt(r.charAt(1),16)/15,parseInt(r.charAt(2),16)/15,t);if(a===6)return this.setHex(parseInt(r,16),t);Ve("Color: Invalid hex color "+e)}else if(e&&e.length>0)return this.setColorName(e,t);return this}setColorName(e,t=pt){let n=xp[e.toLowerCase()];return n!==void 0?this.setHex(n,t):Ve("Color: Unknown color "+e),this}clone(){return new this.constructor(this.r,this.g,this.b)}copy(e){return this.r=e.r,this.g=e.g,this.b=e.b,this}copySRGBToLinear(e){return this.r=Ji(e.r),this.g=Ji(e.g),this.b=Ji(e.b),this}copyLinearToSRGB(e){return this.r=wr(e.r),this.g=wr(e.g),this.b=wr(e.b),this}convertSRGBToLinear(){return this.copySRGBToLinear(this),this}convertLinearToSRGB(){return this.copyLinearToSRGB(this),this}getHex(e=pt){return lt.workingToColorSpace(Tn.copy(this),e),Math.round(it(Tn.r*255,0,255))*65536+Math.round(it(Tn.g*255,0,255))*256+Math.round(it(Tn.b*255,0,255))}getHexString(e=pt){return("000000"+this.getHex(e).toString(16)).slice(-6)}getHSL(e,t=lt.workingColorSpace){lt.workingToColorSpace(Tn.copy(this),t);let n=Tn.r,s=Tn.g,r=Tn.b,a=Math.max(n,s,r),o=Math.min(n,s,r),c,l,h=(o+a)/2;if(o===a)c=0,l=0;else{let u=a-o;switch(l=h<=.5?u/(a+o):u/(2-a-o),a){case n:c=(s-r)/u+(s<r?6:0);break;case s:c=(r-n)/u+2;break;case r:c=(n-s)/u+4;break}c/=6}return e.h=c,e.s=l,e.l=h,e}getRGB(e,t=lt.workingColorSpace){return lt.workingToColorSpace(Tn.copy(this),t),e.r=Tn.r,e.g=Tn.g,e.b=Tn.b,e}getStyle(e=pt){lt.workingToColorSpace(Tn.copy(this),e);let t=Tn.r,n=Tn.g,s=Tn.b;return e!==pt?`color(${e} ${t.toFixed(3)} ${n.toFixed(3)} ${s.toFixed(3)})`:`rgb(${Math.round(t*255)},${Math.round(n*255)},${Math.round(s*255)})`}offsetHSL(e,t,n){return this.getHSL(fs),this.setHSL(fs.h+e,fs.s+t,fs.l+n)}add(e){return this.r+=e.r,this.g+=e.g,this.b+=e.b,this}addColors(e,t){return this.r=e.r+t.r,this.g=e.g+t.g,this.b=e.b+t.b,this}addScalar(e){return this.r+=e,this.g+=e,this.b+=e,this}sub(e){return this.r=Math.max(0,this.r-e.r),this.g=Math.max(0,this.g-e.g),this.b=Math.max(0,this.b-e.b),this}multiply(e){return this.r*=e.r,this.g*=e.g,this.b*=e.b,this}multiplyScalar(e){return this.r*=e,this.g*=e,this.b*=e,this}lerp(e,t){return this.r+=(e.r-this.r)*t,this.g+=(e.g-this.g)*t,this.b+=(e.b-this.b)*t,this}lerpColors(e,t,n){return this.r=e.r+(t.r-e.r)*n,this.g=e.g+(t.g-e.g)*n,this.b=e.b+(t.b-e.b)*n,this}lerpHSL(e,t){this.getHSL(fs),e.getHSL(Io);let n=va(fs.h,Io.h,t),s=va(fs.s,Io.s,t),r=va(fs.l,Io.l,t);return this.setHSL(n,s,r),this}setFromVector3(e){return this.r=e.x,this.g=e.y,this.b=e.z,this}applyMatrix3(e){let t=this.r,n=this.g,s=this.b,r=e.elements;return this.r=r[0]*t+r[3]*n+r[6]*s,this.g=r[1]*t+r[4]*n+r[7]*s,this.b=r[2]*t+r[5]*n+r[8]*s,this}equals(e){return e.r===this.r&&e.g===this.g&&e.b===this.b}fromArray(e,t=0){return this.r=e[t],this.g=e[t+1],this.b=e[t+2],this}toArray(e=[],t=0){return e[t]=this.r,e[t+1]=this.g,e[t+2]=this.b,e}fromBufferAttribute(e,t){return this.r=e.getX(t),this.g=e.getY(t),this.b=e.getZ(t),this}toJSON(){return this.getHex()}*[Symbol.iterator](){yield this.r,yield this.g,yield this.b}},Tn=new Be;Be.NAMES=xp;var Xs=class extends At{constructor(){super(),this.isScene=!0,this.type="Scene",this.background=null,this.environment=null,this.fog=null,this.backgroundBlurriness=0,this.backgroundIntensity=1,this.backgroundRotation=new si,this.environmentIntensity=1,this.environmentRotation=new si,this.overrideMaterial=null,typeof __THREE_DEVTOOLS__<"u"&&__THREE_DEVTOOLS__.dispatchEvent(new CustomEvent("observe",{detail:this}))}copy(e,t){return super.copy(e,t),e.background!==null&&(this.background=e.background.clone()),e.environment!==null&&(this.environment=e.environment.clone()),e.fog!==null&&(this.fog=e.fog.clone()),this.backgroundBlurriness=e.backgroundBlurriness,this.backgroundIntensity=e.backgroundIntensity,this.backgroundRotation.copy(e.backgroundRotation),this.environmentIntensity=e.environmentIntensity,this.environmentRotation.copy(e.environmentRotation),e.overrideMaterial!==null&&(this.overrideMaterial=e.overrideMaterial.clone()),this.matrixAutoUpdate=e.matrixAutoUpdate,this}toJSON(e){let t=super.toJSON(e);return this.fog!==null&&(t.object.fog=this.fog.toJSON()),t.object.backgroundBlurriness=this.backgroundBlurriness,t.object.backgroundIntensity=this.backgroundIntensity,t.object.backgroundRotation=this.backgroundRotation.toArray(),t.object.environmentIntensity=this.environmentIntensity,t.object.environmentRotation=this.environmentRotation.toArray(),t}},mi=new P,qi=new P,ch=new P,Xi=new P,gr=new P,br=new P,tf=new P,lh=new P,hh=new P,uh=new P,dh=new Pt,fh=new Pt,ph=new Pt,xs=class i{constructor(e=new P,t=new P,n=new P){this.a=e,this.b=t,this.c=n}static getNormal(e,t,n,s){s.subVectors(n,t),mi.subVectors(e,t),s.cross(mi);let r=s.lengthSq();return r>0?s.multiplyScalar(1/Math.sqrt(r)):s.set(0,0,0)}static getBarycoord(e,t,n,s,r){mi.subVectors(s,t),qi.subVectors(n,t),ch.subVectors(e,t);let a=mi.dot(mi),o=mi.dot(qi),c=mi.dot(ch),l=qi.dot(qi),h=qi.dot(ch),u=a*l-o*o;if(u===0)return r.set(0,0,0),null;let d=1/u,f=(l*c-o*h)*d,m=(a*h-o*c)*d;return r.set(1-f-m,m,f)}static containsPoint(e,t,n,s){return this.getBarycoord(e,t,n,s,Xi)===null?!1:Xi.x>=0&&Xi.y>=0&&Xi.x+Xi.y<=1}static getInterpolation(e,t,n,s,r,a,o,c){return this.getBarycoord(e,t,n,s,Xi)===null?(c.x=0,c.y=0,"z"in c&&(c.z=0),"w"in c&&(c.w=0),null):(c.setScalar(0),c.addScaledVector(r,Xi.x),c.addScaledVector(a,Xi.y),c.addScaledVector(o,Xi.z),c)}static getInterpolatedAttribute(e,t,n,s,r,a){return dh.setScalar(0),fh.setScalar(0),ph.setScalar(0),dh.fromBufferAttribute(e,t),fh.fromBufferAttribute(e,n),ph.fromBufferAttribute(e,s),a.setScalar(0),a.addScaledVector(dh,r.x),a.addScaledVector(fh,r.y),a.addScaledVector(ph,r.z),a}static isFrontFacing(e,t,n,s){return mi.subVectors(n,t),qi.subVectors(e,t),mi.cross(qi).dot(s)<0}set(e,t,n){return this.a.copy(e),this.b.copy(t),this.c.copy(n),this}setFromPointsAndIndices(e,t,n,s){return this.a.copy(e[t]),this.b.copy(e[n]),this.c.copy(e[s]),this}setFromAttributeAndIndices(e,t,n,s){return this.a.fromBufferAttribute(e,t),this.b.fromBufferAttribute(e,n),this.c.fromBufferAttribute(e,s),this}clone(){return new this.constructor().copy(this)}copy(e){return this.a.copy(e.a),this.b.copy(e.b),this.c.copy(e.c),this}getArea(){return mi.subVectors(this.c,this.b),qi.subVectors(this.a,this.b),mi.cross(qi).length()*.5}getMidpoint(e){return e.addVectors(this.a,this.b).add(this.c).multiplyScalar(1/3)}getNormal(e){return i.getNormal(this.a,this.b,this.c,e)}getPlane(e){return e.setFromCoplanarPoints(this.a,this.b,this.c)}getBarycoord(e,t){return i.getBarycoord(e,this.a,this.b,this.c,t)}getInterpolation(e,t,n,s,r){return i.getInterpolation(e,this.a,this.b,this.c,t,n,s,r)}containsPoint(e){return i.containsPoint(e,this.a,this.b,this.c)}isFrontFacing(e){return i.isFrontFacing(this.a,this.b,this.c,e)}intersectsBox(e){return e.intersectsTriangle(this)}closestPointToPoint(e,t){let n=this.a,s=this.b,r=this.c,a,o;gr.subVectors(s,n),br.subVectors(r,n),lh.subVectors(e,n);let c=gr.dot(lh),l=br.dot(lh);if(c<=0&&l<=0)return t.copy(n);hh.subVectors(e,s);let h=gr.dot(hh),u=br.dot(hh);if(h>=0&&u<=h)return t.copy(s);let d=c*u-h*l;if(d<=0&&c>=0&&h<=0)return a=c/(c-h),t.copy(n).addScaledVector(gr,a);uh.subVectors(e,r);let f=gr.dot(uh),m=br.dot(uh);if(m>=0&&f<=m)return t.copy(r);let b=f*l-c*m;if(b<=0&&l>=0&&m<=0)return o=l/(l-m),t.copy(n).addScaledVector(br,o);let g=h*m-f*u;if(g<=0&&u-h>=0&&f-m>=0)return tf.subVectors(r,s),o=(u-h)/(u-h+(f-m)),t.copy(s).addScaledVector(tf,o);let p=1/(g+b+d);return a=b*p,o=d*p,t.copy(n).addScaledVector(gr,a).addScaledVector(br,o)}equals(e){return e.a.equals(this.a)&&e.b.equals(this.b)&&e.c.equals(this.c)}},en=class{constructor(e=new P(1/0,1/0,1/0),t=new P(-1/0,-1/0,-1/0)){this.isBox3=!0,this.min=e,this.max=t}set(e,t){return this.min.copy(e),this.max.copy(t),this}setFromArray(e){this.makeEmpty();for(let t=0,n=e.length;t<n;t+=3)this.expandByPoint(gi.fromArray(e,t));return this}setFromBufferAttribute(e){this.makeEmpty();for(let t=0,n=e.count;t<n;t++)this.expandByPoint(gi.fromBufferAttribute(e,t));return this}setFromPoints(e){this.makeEmpty();for(let t=0,n=e.length;t<n;t++)this.expandByPoint(e[t]);return this}setFromCenterAndSize(e,t){let n=gi.copy(t).multiplyScalar(.5);return this.min.copy(e).sub(n),this.max.copy(e).add(n),this}setFromObject(e,t=!1){return this.makeEmpty(),this.expandByObject(e,t)}clone(){return new this.constructor().copy(this)}copy(e){return this.min.copy(e.min),this.max.copy(e.max),this}makeEmpty(){return this.min.x=this.min.y=this.min.z=1/0,this.max.x=this.max.y=this.max.z=-1/0,this}isEmpty(){return this.max.x<this.min.x||this.max.y<this.min.y||this.max.z<this.min.z}getCenter(e){return this.isEmpty()?e.set(0,0,0):e.addVectors(this.min,this.max).multiplyScalar(.5)}getSize(e){return this.isEmpty()?e.set(0,0,0):e.subVectors(this.max,this.min)}expandByPoint(e){return this.min.min(e),this.max.max(e),this}expandByVector(e){return this.min.sub(e),this.max.add(e),this}expandByScalar(e){return this.min.addScalar(-e),this.max.addScalar(e),this}expandByObject(e,t=!1){e.updateWorldMatrix(!1,!1);let n=e.geometry;if(n!==void 0){let r=n.getAttribute("position");if(t===!0&&r!==void 0&&e.isInstancedMesh!==!0)for(let a=0,o=r.count;a<o;a++)e.isMesh===!0?e.getVertexPosition(a,gi):gi.fromBufferAttribute(r,a),gi.applyMatrix4(e.matrixWorld),this.expandByPoint(gi);else e.boundingBox!==void 0?(e.boundingBox===null&&e.computeBoundingBox(),Lo.copy(e.boundingBox)):(n.boundingBox===null&&n.computeBoundingBox(),Lo.copy(n.boundingBox)),Lo.applyMatrix4(e.matrixWorld),this.union(Lo)}let s=e.children;for(let r=0,a=s.length;r<a;r++)this.expandByObject(s[r],t);return this}containsPoint(e){return e.x>=this.min.x&&e.x<=this.max.x&&e.y>=this.min.y&&e.y<=this.max.y&&e.z>=this.min.z&&e.z<=this.max.z}containsBox(e){return this.min.x<=e.min.x&&e.max.x<=this.max.x&&this.min.y<=e.min.y&&e.max.y<=this.max.y&&this.min.z<=e.min.z&&e.max.z<=this.max.z}getParameter(e,t){return t.set((e.x-this.min.x)/(this.max.x-this.min.x),(e.y-this.min.y)/(this.max.y-this.min.y),(e.z-this.min.z)/(this.max.z-this.min.z))}intersectsBox(e){return e.max.x>=this.min.x&&e.min.x<=this.max.x&&e.max.y>=this.min.y&&e.min.y<=this.max.y&&e.max.z>=this.min.z&&e.min.z<=this.max.z}intersectsSphere(e){return this.clampPoint(e.center,gi),gi.distanceToSquared(e.center)<=e.radius*e.radius}intersectsPlane(e){let t,n;return e.normal.x>0?(t=e.normal.x*this.min.x,n=e.normal.x*this.max.x):(t=e.normal.x*this.max.x,n=e.normal.x*this.min.x),e.normal.y>0?(t+=e.normal.y*this.min.y,n+=e.normal.y*this.max.y):(t+=e.normal.y*this.max.y,n+=e.normal.y*this.min.y),e.normal.z>0?(t+=e.normal.z*this.min.z,n+=e.normal.z*this.max.z):(t+=e.normal.z*this.max.z,n+=e.normal.z*this.min.z),t<=-e.constant&&n>=-e.constant}intersectsTriangle(e){if(this.isEmpty())return!1;this.getCenter(da),Do.subVectors(this.max,da),xr.subVectors(e.a,da),_r.subVectors(e.b,da),yr.subVectors(e.c,da),ps.subVectors(_r,xr),ms.subVectors(yr,_r),Bs.subVectors(xr,yr);let t=[0,-ps.z,ps.y,0,-ms.z,ms.y,0,-Bs.z,Bs.y,ps.z,0,-ps.x,ms.z,0,-ms.x,Bs.z,0,-Bs.x,-ps.y,ps.x,0,-ms.y,ms.x,0,-Bs.y,Bs.x,0];return!mh(t,xr,_r,yr,Do)||(t=[1,0,0,0,1,0,0,0,1],!mh(t,xr,_r,yr,Do))?!1:(No.crossVectors(ps,ms),t=[No.x,No.y,No.z],mh(t,xr,_r,yr,Do))}clampPoint(e,t){return t.copy(e).clamp(this.min,this.max)}distanceToPoint(e){return this.clampPoint(e,gi).distanceTo(e)}getBoundingSphere(e){return this.isEmpty()?e.makeEmpty():(this.getCenter(e.center),e.radius=this.getSize(gi).length()*.5),e}intersect(e){return this.min.max(e.min),this.max.min(e.max),this.isEmpty()&&this.makeEmpty(),this}union(e){return this.min.min(e.min),this.max.max(e.max),this}applyMatrix4(e){return this.isEmpty()?this:(ji[0].set(this.min.x,this.min.y,this.min.z).applyMatrix4(e),ji[1].set(this.min.x,this.min.y,this.max.z).applyMatrix4(e),ji[2].set(this.min.x,this.max.y,this.min.z).applyMatrix4(e),ji[3].set(this.min.x,this.max.y,this.max.z).applyMatrix4(e),ji[4].set(this.max.x,this.min.y,this.min.z).applyMatrix4(e),ji[5].set(this.max.x,this.min.y,this.max.z).applyMatrix4(e),ji[6].set(this.max.x,this.max.y,this.min.z).applyMatrix4(e),ji[7].set(this.max.x,this.max.y,this.max.z).applyMatrix4(e),this.setFromPoints(ji),this)}translate(e){return this.min.add(e),this.max.add(e),this}equals(e){return e.min.equals(this.min)&&e.max.equals(this.max)}toJSON(){return{min:this.min.toArray(),max:this.max.toArray()}}fromJSON(e){return this.min.fromArray(e.min),this.max.fromArray(e.max),this}},ji=[new P,new P,new P,new P,new P,new P,new P,new P],gi=new P,Lo=new en,xr=new P,_r=new P,yr=new P,ps=new P,ms=new P,Bs=new P,da=new P,Do=new P,No=new P,ks=new P;function mh(i,e,t,n,s){for(let r=0,a=i.length-3;r<=a;r+=3){ks.fromArray(i,r);let o=s.x*Math.abs(ks.x)+s.y*Math.abs(ks.y)+s.z*Math.abs(ks.z),c=e.dot(ks),l=t.dot(ks),h=n.dot(ks);if(Math.max(-Math.max(c,l,h),Math.min(c,l,h))>o)return!1}return!0}var rn=new P,Fo=new ae,Em=0,an=class extends _i{constructor(e,t,n=!1){if(super(),Array.isArray(e))throw new TypeError("THREE.BufferAttribute: array should be a Typed Array.");this.isBufferAttribute=!0,Object.defineProperty(this,"id",{value:Em++}),this.name="",this.array=e,this.itemSize=t,this.count=e!==void 0?e.length/t:0,this.normalized=n,this.usage=uu,this.updateRanges=[],this.gpuType=Xn,this.version=0}onUploadCallback(){}set needsUpdate(e){e===!0&&this.version++}setUsage(e){return this.usage=e,this}addUpdateRange(e,t){this.updateRanges.push({start:e,count:t})}clearUpdateRanges(){this.updateRanges.length=0}copy(e){return this.name=e.name,this.array=new e.array.constructor(e.array),this.itemSize=e.itemSize,this.count=e.count,this.normalized=e.normalized,this.usage=e.usage,this.gpuType=e.gpuType,this}copyAt(e,t,n){e*=this.itemSize,n*=t.itemSize;for(let s=0,r=this.itemSize;s<r;s++)this.array[e+s]=t.array[n+s];return this}copyArray(e){return this.array.set(e),this}applyMatrix3(e){if(this.itemSize===2)for(let t=0,n=this.count;t<n;t++)Fo.fromBufferAttribute(this,t),Fo.applyMatrix3(e),this.setXY(t,Fo.x,Fo.y);else if(this.itemSize===3)for(let t=0,n=this.count;t<n;t++)rn.fromBufferAttribute(this,t),rn.applyMatrix3(e),this.setXYZ(t,rn.x,rn.y,rn.z);return this}applyMatrix4(e){for(let t=0,n=this.count;t<n;t++)rn.fromBufferAttribute(this,t),rn.applyMatrix4(e),this.setXYZ(t,rn.x,rn.y,rn.z);return this}applyNormalMatrix(e){for(let t=0,n=this.count;t<n;t++)rn.fromBufferAttribute(this,t),rn.applyNormalMatrix(e),this.setXYZ(t,rn.x,rn.y,rn.z);return this}transformDirection(e){for(let t=0,n=this.count;t<n;t++)rn.fromBufferAttribute(this,t),rn.transformDirection(e),this.setXYZ(t,rn.x,rn.y,rn.z);return this}set(e,t=0){return this.array.set(e,t),this}getComponent(e,t){let n=this.array[e*this.itemSize+t];return this.normalized&&(n=bi(n,this.array)),n}setComponent(e,t,n){return this.normalized&&(n=Ct(n,this.array)),this.array[e*this.itemSize+t]=n,this}getX(e){let t=this.array[e*this.itemSize];return this.normalized&&(t=bi(t,this.array)),t}setX(e,t){return this.normalized&&(t=Ct(t,this.array)),this.array[e*this.itemSize]=t,this}getY(e){let t=this.array[e*this.itemSize+1];return this.normalized&&(t=bi(t,this.array)),t}setY(e,t){return this.normalized&&(t=Ct(t,this.array)),this.array[e*this.itemSize+1]=t,this}getZ(e){let t=this.array[e*this.itemSize+2];return this.normalized&&(t=bi(t,this.array)),t}setZ(e,t){return this.normalized&&(t=Ct(t,this.array)),this.array[e*this.itemSize+2]=t,this}getW(e){let t=this.array[e*this.itemSize+3];return this.normalized&&(t=bi(t,this.array)),t}setW(e,t){return this.normalized&&(t=Ct(t,this.array)),this.array[e*this.itemSize+3]=t,this}setXY(e,t,n){return e*=this.itemSize,this.normalized&&(t=Ct(t,this.array),n=Ct(n,this.array)),this.array[e+0]=t,this.array[e+1]=n,this}setXYZ(e,t,n,s){return e*=this.itemSize,this.normalized&&(t=Ct(t,this.array),n=Ct(n,this.array),s=Ct(s,this.array)),this.array[e+0]=t,this.array[e+1]=n,this.array[e+2]=s,this}setXYZW(e,t,n,s,r){return e*=this.itemSize,this.normalized&&(t=Ct(t,this.array),n=Ct(n,this.array),s=Ct(s,this.array),r=Ct(r,this.array)),this.array[e+0]=t,this.array[e+1]=n,this.array[e+2]=s,this.array[e+3]=r,this}onUpload(e){return this.onUploadCallback=e,this}clone(){return new this.constructor(this.array,this.itemSize).copy(this)}toJSON(){let e={itemSize:this.itemSize,type:this.array.constructor.name,array:Array.from(this.array),normalized:this.normalized};return e.name=this.name,e.usage=this.usage,e.gpuType=this.gpuType,e}dispose(){this.dispatchEvent({type:"dispose"})}};var Aa=class extends an{constructor(e,t,n){super(new Uint16Array(e),t,n)}};var Ra=class extends an{constructor(e,t,n){super(new Uint32Array(e),t,n)}};var nt=class extends an{constructor(e,t,n){super(new Float32Array(e),t,n)}},Tm=new en,fa=new P,gh=new P,mn=class{constructor(e=new P,t=-1){this.isSphere=!0,this.center=e,this.radius=t}set(e,t){return this.center.copy(e),this.radius=t,this}setFromPoints(e,t){let n=this.center;t!==void 0?n.copy(t):Tm.setFromPoints(e).getCenter(n);let s=0;for(let r=0,a=e.length;r<a;r++)s=Math.max(s,n.distanceToSquared(e[r]));return this.radius=Math.sqrt(s),this}copy(e){return this.center.copy(e.center),this.radius=e.radius,this}isEmpty(){return this.radius<0}makeEmpty(){return this.center.set(0,0,0),this.radius=-1,this}containsPoint(e){return e.distanceToSquared(this.center)<=this.radius*this.radius}distanceToPoint(e){return e.distanceTo(this.center)-this.radius}intersectsSphere(e){let t=this.radius+e.radius;return e.center.distanceToSquared(this.center)<=t*t}intersectsBox(e){return e.intersectsSphere(this)}intersectsPlane(e){return Math.abs(e.distanceToPoint(this.center))<=this.radius}clampPoint(e,t){let n=this.center.distanceToSquared(e);return t.copy(e),n>this.radius*this.radius&&(t.sub(this.center).normalize(),t.multiplyScalar(this.radius).add(this.center)),t}getBoundingBox(e){return this.isEmpty()?(e.makeEmpty(),e):(e.set(this.center,this.center),e.expandByScalar(this.radius),e)}applyMatrix4(e){return this.center.applyMatrix4(e),this.radius=this.radius*e.getMaxScaleOnAxis(),this}translate(e){return this.center.add(e),this}expandByPoint(e){if(this.isEmpty())return this.center.copy(e),this.radius=0,this;fa.subVectors(e,this.center);let t=fa.lengthSq();if(t>this.radius*this.radius){let n=Math.sqrt(t),s=(n-this.radius)*.5;this.center.addScaledVector(fa,s/n),this.radius+=s}return this}union(e){return e.isEmpty()?this:this.isEmpty()?(this.copy(e),this):(this.center.equals(e.center)===!0?this.radius=Math.max(this.radius,e.radius):(gh.subVectors(e.center,this.center).setLength(e.radius),this.expandByPoint(fa.copy(e.center).add(gh)),this.expandByPoint(fa.copy(e.center).sub(gh))),this)}equals(e){return e.center.equals(this.center)&&e.radius===this.radius}clone(){return new this.constructor().copy(this)}toJSON(){return{radius:this.radius,center:this.center.toArray()}}fromJSON(e){return this.radius=e.radius,this.center.fromArray(e.center),this}},wm=0,ti=new Ze,bh=new At,vr=new P,zn=new en,pa=new en,pn=new P,Bt=class i extends _i{constructor(){super(),this.isBufferGeometry=!0,Object.defineProperty(this,"id",{value:wm++}),this.uuid=ii(),this.name="",this.type="BufferGeometry",this.index=null,this.indirect=null,this.indirectOffset=0,this.attributes={},this.morphAttributes={},this.morphTargetsRelative=!1,this.groups=[],this.boundingBox=null,this.boundingSphere=null,this.drawRange={start:0,count:1/0},this.userData={},this._transformed=!1}getIndex(){return this.index}setIndex(e){return Array.isArray(e)?this.index=new(J0(e)?Ra:Aa)(e,1):this.index=e,this}setIndirect(e,t=0){return this.indirect=e,this.indirectOffset=t,this}getIndirect(){return this.indirect}getAttribute(e){return this.attributes[e]}setAttribute(e,t){return this.attributes[e]=t,this}deleteAttribute(e){return delete this.attributes[e],this}hasAttribute(e){return this.attributes[e]!==void 0}addGroup(e,t,n=0){this.groups.push({start:e,count:t,materialIndex:n})}clearGroups(){this.groups=[]}setDrawRange(e,t){this.drawRange.start=e,this.drawRange.count=t}applyMatrix4(e){let t=this.attributes.position;t!==void 0&&(t.applyMatrix4(e),t.needsUpdate=!0);let n=this.attributes.normal;if(n!==void 0){let r=new et().getNormalMatrix(e);n.applyNormalMatrix(r),n.needsUpdate=!0}let s=this.attributes.tangent;return s!==void 0&&(s.transformDirection(e),s.needsUpdate=!0),this.boundingBox!==null&&this.computeBoundingBox(),this.boundingSphere!==null&&this.computeBoundingSphere(),this._transformed=!0,this}applyQuaternion(e){return ti.makeRotationFromQuaternion(e),this.applyMatrix4(ti),this}rotateX(e){return ti.makeRotationX(e),this.applyMatrix4(ti),this}rotateY(e){return ti.makeRotationY(e),this.applyMatrix4(ti),this}rotateZ(e){return ti.makeRotationZ(e),this.applyMatrix4(ti),this}translate(e,t,n){return ti.makeTranslation(e,t,n),this.applyMatrix4(ti),this}scale(e,t,n){return ti.makeScale(e,t,n),this.applyMatrix4(ti),this}lookAt(e){return bh.lookAt(e),bh.updateMatrix(),this.applyMatrix4(bh.matrix),this}center(){return this.computeBoundingBox(),this.boundingBox.getCenter(vr).negate(),this.translate(vr.x,vr.y,vr.z),this}setFromPoints(e){let t=this.getAttribute("position");if(t===void 0){let n=[];for(let s=0,r=e.length;s<r;s++){let a=e[s];n.push(a.x,a.y,a.z||0)}this.setAttribute("position",new nt(n,3))}else{let n=Math.min(e.length,t.count);for(let s=0;s<n;s++){let r=e[s];t.setXYZ(s,r.x,r.y,r.z||0)}e.length>t.count&&Ve("BufferGeometry: Buffer size too small for points data. Use .dispose() and create a new geometry."),t.needsUpdate=!0}return this}computeBoundingBox(){this.boundingBox===null&&(this.boundingBox=new en);let e=this.attributes.position,t=this.morphAttributes.position;if(e&&e.isGLBufferAttribute){Ye("BufferGeometry.computeBoundingBox(): GLBufferAttribute requires a manual bounding box.",this),this.boundingBox.set(new P(-1/0,-1/0,-1/0),new P(1/0,1/0,1/0));return}if(e!==void 0){if(this.boundingBox.setFromBufferAttribute(e),t)for(let n=0,s=t.length;n<s;n++){let r=t[n];zn.setFromBufferAttribute(r),this.morphTargetsRelative?(pn.addVectors(this.boundingBox.min,zn.min),this.boundingBox.expandByPoint(pn),pn.addVectors(this.boundingBox.max,zn.max),this.boundingBox.expandByPoint(pn)):(this.boundingBox.expandByPoint(zn.min),this.boundingBox.expandByPoint(zn.max))}}else this.boundingBox.makeEmpty();(isNaN(this.boundingBox.min.x)||isNaN(this.boundingBox.min.y)||isNaN(this.boundingBox.min.z))&&Ye('BufferGeometry.computeBoundingBox(): Computed min/max have NaN values. The "position" attribute is likely to have NaN values.',this)}computeBoundingSphere(){this.boundingSphere===null&&(this.boundingSphere=new mn);let e=this.attributes.position,t=this.morphAttributes.position;if(e&&e.isGLBufferAttribute){Ye("BufferGeometry.computeBoundingSphere(): GLBufferAttribute requires a manual bounding sphere.",this),this.boundingSphere.set(new P,1/0);return}if(e){let n=this.boundingSphere.center;if(zn.setFromBufferAttribute(e),t)for(let r=0,a=t.length;r<a;r++){let o=t[r];pa.setFromBufferAttribute(o),this.morphTargetsRelative?(pn.addVectors(zn.min,pa.min),zn.expandByPoint(pn),pn.addVectors(zn.max,pa.max),zn.expandByPoint(pn)):(zn.expandByPoint(pa.min),zn.expandByPoint(pa.max))}zn.getCenter(n);let s=0;for(let r=0,a=e.count;r<a;r++)pn.fromBufferAttribute(e,r),s=Math.max(s,n.distanceToSquared(pn));if(t)for(let r=0,a=t.length;r<a;r++){let o=t[r],c=this.morphTargetsRelative;for(let l=0,h=o.count;l<h;l++)pn.fromBufferAttribute(o,l),c&&(vr.fromBufferAttribute(e,l),pn.add(vr)),s=Math.max(s,n.distanceToSquared(pn))}this.boundingSphere.radius=Math.sqrt(s),isNaN(this.boundingSphere.radius)&&Ye('BufferGeometry.computeBoundingSphere(): Computed radius is NaN. The "position" attribute is likely to have NaN values.',this)}}computeTangents(){let e=this.index,t=this.attributes;if(e===null||t.position===void 0||t.normal===void 0||t.uv===void 0){Ye("BufferGeometry: .computeTangents() failed. Missing required attributes (index, position, normal or uv)");return}let n=t.position,s=t.normal,r=t.uv,a=this.getAttribute("tangent");(a===void 0||a.count!==n.count)&&(a=new an(new Float32Array(4*n.count),4),this.setAttribute("tangent",a));let o=[],c=[];for(let y=0;y<n.count;y++)o[y]=new P,c[y]=new P;let l=new P,h=new P,u=new P,d=new ae,f=new ae,m=new ae,b=new P,g=new P;function p(y,w,R){l.fromBufferAttribute(n,y),h.fromBufferAttribute(n,w),u.fromBufferAttribute(n,R),d.fromBufferAttribute(r,y),f.fromBufferAttribute(r,w),m.fromBufferAttribute(r,R),h.sub(l),u.sub(l),f.sub(d),m.sub(d);let I=1/(f.x*m.y-m.x*f.y);isFinite(I)&&(b.copy(h).multiplyScalar(m.y).addScaledVector(u,-f.y).multiplyScalar(I),g.copy(u).multiplyScalar(f.x).addScaledVector(h,-m.x).multiplyScalar(I),o[y].add(b),o[w].add(b),o[R].add(b),c[y].add(g),c[w].add(g),c[R].add(g))}let _=this.groups;_.length===0&&(_=[{start:0,count:e.count}]);for(let y=0,w=_.length;y<w;++y){let R=_[y],I=R.start,L=R.count;for(let F=I,D=I+L;F<D;F+=3)p(e.getX(F+0),e.getX(F+1),e.getX(F+2))}let S=new P,x=new P,M=new P,E=new P;function A(y){M.fromBufferAttribute(s,y),E.copy(M);let w=o[y];S.copy(w),S.sub(M.multiplyScalar(M.dot(w))).normalize(),x.crossVectors(E,w);let I=x.dot(c[y])<0?-1:1;a.setXYZW(y,S.x,S.y,S.z,I)}for(let y=0,w=_.length;y<w;++y){let R=_[y],I=R.start,L=R.count;for(let F=I,D=I+L;F<D;F+=3)A(e.getX(F+0)),A(e.getX(F+1)),A(e.getX(F+2))}this._transformed=!0}computeVertexNormals(){let e=this.index,t=this.getAttribute("position");if(t!==void 0){let n=this.getAttribute("normal");if(n===void 0||n.count!==t.count)n=new an(new Float32Array(t.count*3),3),this.setAttribute("normal",n);else for(let d=0,f=n.count;d<f;d++)n.setXYZ(d,0,0,0);let s=new P,r=new P,a=new P,o=new P,c=new P,l=new P,h=new P,u=new P;if(e)for(let d=0,f=e.count;d<f;d+=3){let m=e.getX(d+0),b=e.getX(d+1),g=e.getX(d+2);s.fromBufferAttribute(t,m),r.fromBufferAttribute(t,b),a.fromBufferAttribute(t,g),h.subVectors(a,r),u.subVectors(s,r),h.cross(u),o.fromBufferAttribute(n,m),c.fromBufferAttribute(n,b),l.fromBufferAttribute(n,g),o.add(h),c.add(h),l.add(h),n.setXYZ(m,o.x,o.y,o.z),n.setXYZ(b,c.x,c.y,c.z),n.setXYZ(g,l.x,l.y,l.z)}else for(let d=0,f=t.count;d<f;d+=3)s.fromBufferAttribute(t,d+0),r.fromBufferAttribute(t,d+1),a.fromBufferAttribute(t,d+2),h.subVectors(a,r),u.subVectors(s,r),h.cross(u),n.setXYZ(d+0,h.x,h.y,h.z),n.setXYZ(d+1,h.x,h.y,h.z),n.setXYZ(d+2,h.x,h.y,h.z);this.normalizeNormals(),n.needsUpdate=!0}}normalizeNormals(){let e=this.attributes.normal;for(let t=0,n=e.count;t<n;t++)pn.fromBufferAttribute(e,t),pn.normalize(),e.setXYZ(t,pn.x,pn.y,pn.z)}toNonIndexed(){function e(o,c){let l=o.array,h=o.itemSize,u=o.normalized,d=new l.constructor(c.length*h),f=0,m=0;for(let b=0,g=c.length;b<g;b++){o.isInterleavedBufferAttribute?f=c[b]*o.data.stride+o.offset:f=c[b]*h;for(let p=0;p<h;p++)d[m++]=l[f++]}return new an(d,h,u)}if(this.index===null)return Ve("BufferGeometry.toNonIndexed(): BufferGeometry is already non-indexed."),this;let t=new i,n=this.index.array,s=this.attributes;for(let o in s){let c=s[o],l=e(c,n);t.setAttribute(o,l)}let r=this.morphAttributes;for(let o in r){let c=[],l=r[o];for(let h=0,u=l.length;h<u;h++){let d=l[h],f=e(d,n);c.push(f)}t.morphAttributes[o]=c}t.morphTargetsRelative=this.morphTargetsRelative;let a=this.groups;for(let o=0,c=a.length;o<c;o++){let l=a[o];t.addGroup(l.start,l.count,l.materialIndex)}return t}toJSON(){let e={metadata:{version:4.7,type:"BufferGeometry",generator:"BufferGeometry.toJSON"}};if(e.uuid=this.uuid,e.type=this.parameters!==void 0&&this._transformed===!0?"BufferGeometry":this.type,e.name=this.name,Object.keys(this.userData).length>0&&(e.userData=this.userData),this.parameters!==void 0&&this._transformed!==!0){let c=this.parameters;for(let l in c)c[l]!==void 0&&(e[l]=c[l]);return e}e.data={attributes:{}};let t=this.index;t!==null&&(e.data.index={type:t.array.constructor.name,array:Array.prototype.slice.call(t.array)});let n=this.attributes;for(let c in n){let l=n[c];e.data.attributes[c]=l.toJSON(e.data)}let s={},r=!1;for(let c in this.morphAttributes){let l=this.morphAttributes[c],h=[];for(let u=0,d=l.length;u<d;u++){let f=l[u];h.push(f.toJSON(e.data))}h.length>0&&(s[c]=h,r=!0)}r&&(e.data.morphAttributes=s,e.data.morphTargetsRelative=this.morphTargetsRelative);let a=this.groups;a.length>0&&(e.data.groups=JSON.parse(JSON.stringify(a)));let o=this.boundingSphere;return o!==null&&(e.data.boundingSphere=o.toJSON()),e}clone(){return new this.constructor().copy(this)}copy(e){this.index=null,this.attributes={},this.morphAttributes={},this.groups=[],this.boundingBox=null,this.boundingSphere=null;let t={};this.name=e.name;let n=e.index;n!==null&&this.setIndex(n.clone());let s=e.attributes;for(let l in s){let h=s[l];this.setAttribute(l,h.clone(t))}let r=e.morphAttributes;for(let l in r){let h=[],u=r[l];for(let d=0,f=u.length;d<f;d++)h.push(u[d].clone(t));this.morphAttributes[l]=h}this.morphTargetsRelative=e.morphTargetsRelative;let a=e.groups;for(let l=0,h=a.length;l<h;l++){let u=a[l];this.addGroup(u.start,u.count,u.materialIndex)}let o=e.boundingBox;o!==null&&(this.boundingBox=o.clone());let c=e.boundingSphere;return c!==null&&(this.boundingSphere=c.clone()),this.drawRange.start=e.drawRange.start,this.drawRange.count=e.drawRange.count,this.userData=e.userData,this._transformed=e._transformed,this}dispose(){this.dispatchEvent({type:"dispose"})}},Fr=class{constructor(e,t){this.isInterleavedBuffer=!0,this.array=e,this.stride=t,this.count=e!==void 0?e.length/t:0,this.usage=uu,this.updateRanges=[],this.version=0,this.uuid=ii()}onUploadCallback(){}set needsUpdate(e){e===!0&&this.version++}setUsage(e){return this.usage=e,this}addUpdateRange(e,t){this.updateRanges.push({start:e,count:t})}clearUpdateRanges(){this.updateRanges.length=0}copy(e){return this.array=new e.array.constructor(e.array),this.count=e.count,this.stride=e.stride,this.usage=e.usage,this}copyAt(e,t,n){e*=this.stride,n*=t.stride;for(let s=0,r=this.stride;s<r;s++)this.array[e+s]=t.array[n+s];return this}set(e,t=0){return this.array.set(e,t),this}clone(e){e.arrayBuffers===void 0&&(e.arrayBuffers={}),this.array.buffer._uuid===void 0&&(this.array.buffer._uuid=ii()),e.arrayBuffers[this.array.buffer._uuid]===void 0&&(e.arrayBuffers[this.array.buffer._uuid]=this.array.slice(0).buffer);let t=new this.array.constructor(e.arrayBuffers[this.array.buffer._uuid]),n=new this.constructor(t,this.stride);return n.setUsage(this.usage),n}onUpload(e){return this.onUploadCallback=e,this}toJSON(e){e.arrayBuffers===void 0&&(e.arrayBuffers={}),this.array.buffer._uuid===void 0&&(this.array.buffer._uuid=ii()),e.arrayBuffers[this.array.buffer._uuid]===void 0&&(e.arrayBuffers[this.array.buffer._uuid]=Array.from(new Uint32Array(this.array.buffer)));let t={uuid:this.uuid,buffer:this.array.buffer._uuid,type:this.array.constructor.name,stride:this.stride};return t.usage=this.usage,t}},Ln=new P,Ur=class i{constructor(e,t,n,s=!1){this.isInterleavedBufferAttribute=!0,this.name="",this.data=e,this.itemSize=t,this.offset=n,this.normalized=s}get count(){return this.data.count}get array(){return this.data.array}set needsUpdate(e){this.data.needsUpdate=e}applyMatrix4(e){for(let t=0,n=this.data.count;t<n;t++)Ln.fromBufferAttribute(this,t),Ln.applyMatrix4(e),this.setXYZ(t,Ln.x,Ln.y,Ln.z);return this}applyNormalMatrix(e){for(let t=0,n=this.count;t<n;t++)Ln.fromBufferAttribute(this,t),Ln.applyNormalMatrix(e),this.setXYZ(t,Ln.x,Ln.y,Ln.z);return this}transformDirection(e){for(let t=0,n=this.count;t<n;t++)Ln.fromBufferAttribute(this,t),Ln.transformDirection(e),this.setXYZ(t,Ln.x,Ln.y,Ln.z);return this}getComponent(e,t){let n=this.array[e*this.data.stride+this.offset+t];return this.normalized&&(n=bi(n,this.array)),n}setComponent(e,t,n){return this.normalized&&(n=Ct(n,this.array)),this.data.array[e*this.data.stride+this.offset+t]=n,this}setX(e,t){return this.normalized&&(t=Ct(t,this.array)),this.data.array[e*this.data.stride+this.offset]=t,this}setY(e,t){return this.normalized&&(t=Ct(t,this.array)),this.data.array[e*this.data.stride+this.offset+1]=t,this}setZ(e,t){return this.normalized&&(t=Ct(t,this.array)),this.data.array[e*this.data.stride+this.offset+2]=t,this}setW(e,t){return this.normalized&&(t=Ct(t,this.array)),this.data.array[e*this.data.stride+this.offset+3]=t,this}getX(e){let t=this.data.array[e*this.data.stride+this.offset];return this.normalized&&(t=bi(t,this.array)),t}getY(e){let t=this.data.array[e*this.data.stride+this.offset+1];return this.normalized&&(t=bi(t,this.array)),t}getZ(e){let t=this.data.array[e*this.data.stride+this.offset+2];return this.normalized&&(t=bi(t,this.array)),t}getW(e){let t=this.data.array[e*this.data.stride+this.offset+3];return this.normalized&&(t=bi(t,this.array)),t}setXY(e,t,n){return e=e*this.data.stride+this.offset,this.normalized&&(t=Ct(t,this.array),n=Ct(n,this.array)),this.data.array[e+0]=t,this.data.array[e+1]=n,this}setXYZ(e,t,n,s){return e=e*this.data.stride+this.offset,this.normalized&&(t=Ct(t,this.array),n=Ct(n,this.array),s=Ct(s,this.array)),this.data.array[e+0]=t,this.data.array[e+1]=n,this.data.array[e+2]=s,this}setXYZW(e,t,n,s,r){return e=e*this.data.stride+this.offset,this.normalized&&(t=Ct(t,this.array),n=Ct(n,this.array),s=Ct(s,this.array),r=Ct(r,this.array)),this.data.array[e+0]=t,this.data.array[e+1]=n,this.data.array[e+2]=s,this.data.array[e+3]=r,this}clone(e){if(e===void 0){Ta("InterleavedBufferAttribute.clone(): Cloning an interleaved buffer attribute will de-interleave buffer data.");let t=[];for(let n=0;n<this.count;n++){let s=n*this.data.stride+this.offset;for(let r=0;r<this.itemSize;r++)t.push(this.data.array[s+r])}return new an(new this.array.constructor(t),this.itemSize,this.normalized)}else return e.interleavedBuffers===void 0&&(e.interleavedBuffers={}),e.interleavedBuffers[this.data.uuid]===void 0&&(e.interleavedBuffers[this.data.uuid]=this.data.clone(e)),new i(e.interleavedBuffers[this.data.uuid],this.itemSize,this.offset,this.normalized)}toJSON(e){if(e===void 0){Ta("InterleavedBufferAttribute.toJSON(): Serializing an interleaved buffer attribute will de-interleave buffer data.");let t=[];for(let n=0;n<this.count;n++){let s=n*this.data.stride+this.offset;for(let r=0;r<this.itemSize;r++)t.push(this.data.array[s+r])}return{itemSize:this.itemSize,type:this.array.constructor.name,array:t,normalized:this.normalized}}else return e.interleavedBuffers===void 0&&(e.interleavedBuffers={}),e.interleavedBuffers[this.data.uuid]===void 0&&(e.interleavedBuffers[this.data.uuid]=this.data.toJSON(e)),{isInterleavedBufferAttribute:!0,itemSize:this.itemSize,data:this.data.uuid,offset:this.offset,normalized:this.normalized}}},xh=new P,Am=new P,Rm=new et,Hn=class{constructor(e=new P(1,0,0),t=0){this.isPlane=!0,this.normal=e,this.constant=t}set(e,t){return this.normal.copy(e),this.constant=t,this}setComponents(e,t,n,s){return this.normal.set(e,t,n),this.constant=s,this}setFromNormalAndCoplanarPoint(e,t){return this.normal.copy(e),this.constant=-t.dot(this.normal),this}setFromCoplanarPoints(e,t,n){let s=xh.subVectors(n,t).cross(Am.subVectors(e,t)).normalize();return this.setFromNormalAndCoplanarPoint(s,e),this}copy(e){return this.normal.copy(e.normal),this.constant=e.constant,this}normalize(){let e=1/this.normal.length();return this.normal.multiplyScalar(e),this.constant*=e,this}negate(){return this.constant*=-1,this.normal.negate(),this}distanceToPoint(e){return this.normal.dot(e)+this.constant}distanceToSphere(e){return this.distanceToPoint(e.center)-e.radius}projectPoint(e,t){return t.copy(e).addScaledVector(this.normal,-this.distanceToPoint(e))}intersectLine(e,t,n=!0){let s=e.delta(xh),r=this.normal.dot(s);if(r===0)return this.distanceToPoint(e.start)===0?t.copy(e.start):null;let a=-(e.start.dot(this.normal)+this.constant)/r;return n===!0&&(a<0||a>1)?null:t.copy(e.start).addScaledVector(s,a)}intersectsLine(e){let t=this.distanceToPoint(e.start),n=this.distanceToPoint(e.end);return t<0&&n>0||n<0&&t>0}intersectsBox(e){return e.intersectsPlane(this)}intersectsSphere(e){return e.intersectsPlane(this)}coplanarPoint(e){return e.copy(this.normal).multiplyScalar(-this.constant)}applyMatrix4(e,t){let n=t||Rm.getNormalMatrix(e),s=this.coplanarPoint(xh).applyMatrix4(e),r=this.normal.applyMatrix3(n).normalize();return this.constant=-s.dot(r),this}translate(e){return this.constant-=e.dot(this.normal),this}equals(e){return e.normal.equals(this.normal)&&e.constant===this.constant}clone(){return new this.constructor().copy(this)}toJSON(){return{normal:this.normal.toArray(),constant:this.constant}}fromJSON(e){return this.normal.fromArray(e.normal),this.constant=e.constant,this}},Cm=0,Nn=class extends _i{constructor(){super(),this.isMaterial=!0,Object.defineProperty(this,"id",{value:Cm++}),this.uuid=ii(),this.name="",this.type="Material",this.blending=jr,this.side=Ui,this.vertexColors=!1,this.opacity=1,this.transparent=!1,this.alphaHash=!1,this.blendSrc=Yh,this.blendDst=Jh,this.blendEquation=er,this.blendSrcAlpha=null,this.blendDstAlpha=null,this.blendEquationAlpha=null,this.blendColor=new Be(0,0,0),this.blendAlpha=0,this.depthFunc=Ar,this.depthTest=!0,this.depthWrite=!0,this.stencilWriteMask=255,this.stencilFunc=op,this.stencilRef=0,this.stencilFuncMask=255,this.stencilFail=ic,this.stencilZFail=ic,this.stencilZPass=ic,this.stencilWrite=!1,this.clippingPlanes=null,this.clipIntersection=!1,this.clipShadows=!1,this.shadowSide=null,this.colorWrite=!0,this.precision=null,this.polygonOffset=!1,this.polygonOffsetFactor=0,this.polygonOffsetUnits=0,this.dithering=!1,this.alphaToCoverage=!1,this.premultipliedAlpha=!1,this.forceSinglePass=!1,this.allowOverride=!0,this.visible=!0,this.toneMapped=!0,this.userData={},this.version=0,this._alphaTest=0}get alphaTest(){return this._alphaTest}set alphaTest(e){this._alphaTest>0!=e>0&&this.version++,this._alphaTest=e}onBeforeRender(){}onBeforeCompile(){}customProgramCacheKey(){return this.onBeforeCompile.toString()}setValues(e){if(e!==void 0)for(let t in e){let n=e[t];if(n===void 0){Ve(`Material: parameter '${t}' has value of undefined.`);continue}let s=this[t];if(s===void 0){Ve(`Material: '${t}' is not a property of THREE.${this.type}.`);continue}s&&s.isColor?s.set(n):s&&s.isVector2&&n&&n.isVector2||s&&s.isEuler&&n&&n.isEuler||s&&s.isVector3&&n&&n.isVector3?s.copy(n):this[t]=n}}toJSON(e){let t=e===void 0||typeof e=="string";t&&(e={textures:{},images:{}});let n={metadata:{version:4.7,type:"Material",generator:"Material.toJSON"}};n.uuid=this.uuid,n.type=this.type,n.blending=this.blending,n.side=this.side,n.shadowSide=this.shadowSide,n.vertexColors=this.vertexColors,n.opacity=this.opacity,n.transparent=this.transparent,n.blendSrc=this.blendSrc,n.blendDst=this.blendDst,n.blendEquation=this.blendEquation,n.blendSrcAlpha=this.blendSrcAlpha,n.blendDstAlpha=this.blendDstAlpha,n.blendEquationAlpha=this.blendEquationAlpha,n.blendColor=this.blendColor.getHex(),n.blendAlpha=this.blendAlpha,n.depthFunc=this.depthFunc,n.depthTest=this.depthTest,n.depthWrite=this.depthWrite,n.colorWrite=this.colorWrite,n.clipIntersection=this.clipIntersection,n.clipShadows=this.clipShadows,n.stencilWriteMask=this.stencilWriteMask,n.stencilFunc=this.stencilFunc,n.stencilRef=this.stencilRef,n.stencilFuncMask=this.stencilFuncMask,n.stencilFail=this.stencilFail,n.stencilZFail=this.stencilZFail,n.stencilZPass=this.stencilZPass,n.stencilWrite=this.stencilWrite,n.polygonOffset=this.polygonOffset,n.polygonOffsetFactor=this.polygonOffsetFactor,n.polygonOffsetUnits=this.polygonOffsetUnits,n.dithering=this.dithering,n.alphaTest=this.alphaTest,n.alphaHash=this.alphaHash,n.alphaToCoverage=this.alphaToCoverage,n.premultipliedAlpha=this.premultipliedAlpha,n.forceSinglePass=this.forceSinglePass,n.allowOverride=this.allowOverride,n.visible=this.visible,n.toneMapped=this.toneMapped,n.name=this.name,this.color&&this.color.isColor&&(n.color=this.color.getHex()),this.roughness!==void 0&&(n.roughness=this.roughness),this.metalness!==void 0&&(n.metalness=this.metalness),this.sheen!==void 0&&(n.sheen=this.sheen),this.sheenColor&&this.sheenColor.isColor&&(n.sheenColor=this.sheenColor.getHex()),this.sheenRoughness!==void 0&&(n.sheenRoughness=this.sheenRoughness),this.emissive&&this.emissive.isColor&&(n.emissive=this.emissive.getHex()),this.emissiveIntensity!==void 0&&(n.emissiveIntensity=this.emissiveIntensity),this.specular&&this.specular.isColor&&(n.specular=this.specular.getHex()),this.specularIntensity!==void 0&&(n.specularIntensity=this.specularIntensity),this.specularColor&&this.specularColor.isColor&&(n.specularColor=this.specularColor.getHex()),this.shininess!==void 0&&(n.shininess=this.shininess),this.clearcoat!==void 0&&(n.clearcoat=this.clearcoat),this.clearcoatRoughness!==void 0&&(n.clearcoatRoughness=this.clearcoatRoughness),this.clearcoatMap&&this.clearcoatMap.isTexture&&(n.clearcoatMap=this.clearcoatMap.toJSON(e).uuid),this.clearcoatRoughnessMap&&this.clearcoatRoughnessMap.isTexture&&(n.clearcoatRoughnessMap=this.clearcoatRoughnessMap.toJSON(e).uuid),this.clearcoatNormalMap&&this.clearcoatNormalMap.isTexture&&(n.clearcoatNormalMap=this.clearcoatNormalMap.toJSON(e).uuid,n.clearcoatNormalScale=this.clearcoatNormalScale.toArray()),this.sheenColorMap&&this.sheenColorMap.isTexture&&(n.sheenColorMap=this.sheenColorMap.toJSON(e).uuid),this.sheenRoughnessMap&&this.sheenRoughnessMap.isTexture&&(n.sheenRoughnessMap=this.sheenRoughnessMap.toJSON(e).uuid),this.dispersion!==void 0&&(n.dispersion=this.dispersion),this.retroreflectivity!==void 0&&(n.retroreflectivity=this.retroreflectivity),this.iridescence!==void 0&&(n.iridescence=this.iridescence),this.iridescenceIOR!==void 0&&(n.iridescenceIOR=this.iridescenceIOR),this.iridescenceThicknessRange!==void 0&&(n.iridescenceThicknessRange=this.iridescenceThicknessRange),this.iridescenceMap&&this.iridescenceMap.isTexture&&(n.iridescenceMap=this.iridescenceMap.toJSON(e).uuid),this.iridescenceThicknessMap&&this.iridescenceThicknessMap.isTexture&&(n.iridescenceThicknessMap=this.iridescenceThicknessMap.toJSON(e).uuid),this.anisotropy!==void 0&&(n.anisotropy=this.anisotropy),this.anisotropyRotation!==void 0&&(n.anisotropyRotation=this.anisotropyRotation),this.anisotropyMap&&this.anisotropyMap.isTexture&&(n.anisotropyMap=this.anisotropyMap.toJSON(e).uuid),this.map&&this.map.isTexture&&(n.map=this.map.toJSON(e).uuid),this.matcap&&this.matcap.isTexture&&(n.matcap=this.matcap.toJSON(e).uuid),this.alphaMap&&this.alphaMap.isTexture&&(n.alphaMap=this.alphaMap.toJSON(e).uuid),this.lightMap&&this.lightMap.isTexture&&(n.lightMap=this.lightMap.toJSON(e).uuid,n.lightMapIntensity=this.lightMapIntensity),this.aoMap&&this.aoMap.isTexture&&(n.aoMap=this.aoMap.toJSON(e).uuid,n.aoMapIntensity=this.aoMapIntensity),this.bumpMap&&this.bumpMap.isTexture&&(n.bumpMap=this.bumpMap.toJSON(e).uuid,n.bumpScale=this.bumpScale),this.normalMap&&this.normalMap.isTexture&&(n.normalMap=this.normalMap.toJSON(e).uuid,n.normalMapType=this.normalMapType,n.normalScale=this.normalScale.toArray()),this.displacementMap&&this.displacementMap.isTexture&&(n.displacementMap=this.displacementMap.toJSON(e).uuid,n.displacementScale=this.displacementScale,n.displacementBias=this.displacementBias),this.roughnessMap&&this.roughnessMap.isTexture&&(n.roughnessMap=this.roughnessMap.toJSON(e).uuid),this.metalnessMap&&this.metalnessMap.isTexture&&(n.metalnessMap=this.metalnessMap.toJSON(e).uuid),this.emissiveMap&&this.emissiveMap.isTexture&&(n.emissiveMap=this.emissiveMap.toJSON(e).uuid),this.specularMap&&this.specularMap.isTexture&&(n.specularMap=this.specularMap.toJSON(e).uuid),this.specularIntensityMap&&this.specularIntensityMap.isTexture&&(n.specularIntensityMap=this.specularIntensityMap.toJSON(e).uuid),this.specularColorMap&&this.specularColorMap.isTexture&&(n.specularColorMap=this.specularColorMap.toJSON(e).uuid),this.envMap&&this.envMap.isTexture&&(n.envMap=this.envMap.toJSON(e).uuid,this.combine!==void 0&&(n.combine=this.combine)),this.envMapRotation!==void 0&&(n.envMapRotation=this.envMapRotation.toArray()),this.envMapIntensity!==void 0&&(n.envMapIntensity=this.envMapIntensity),this.reflectivity!==void 0&&(n.reflectivity=this.reflectivity),this.refractionRatio!==void 0&&(n.refractionRatio=this.refractionRatio),this.gradientMap&&this.gradientMap.isTexture&&(n.gradientMap=this.gradientMap.toJSON(e).uuid),this.transmission!==void 0&&(n.transmission=this.transmission),this.transmissionMap&&this.transmissionMap.isTexture&&(n.transmissionMap=this.transmissionMap.toJSON(e).uuid),this.thickness!==void 0&&(n.thickness=this.thickness),this.thicknessMap&&this.thicknessMap.isTexture&&(n.thicknessMap=this.thicknessMap.toJSON(e).uuid),this.attenuationDistance!==void 0&&(n.attenuationDistance=this.attenuationDistance),this.attenuationColor!==void 0&&(n.attenuationColor=this.attenuationColor.getHex()),this.size!==void 0&&(n.size=this.size),this.sizeAttenuation!==void 0&&(n.sizeAttenuation=this.sizeAttenuation),Array.isArray(this.clippingPlanes)&&this.clippingPlanes.length>0&&(n.clippingPlanes=this.clippingPlanes.map(r=>r.toJSON())),this.rotation!==void 0&&(n.rotation=this.rotation),this.depthPacking!==void 0&&(n.depthPacking=this.depthPacking),this.linewidth!==void 0&&(n.linewidth=this.linewidth),this.linecap!==void 0&&(n.linecap=this.linecap),this.linejoin!==void 0&&(n.linejoin=this.linejoin),this.dashSize!==void 0&&(n.dashSize=this.dashSize),this.gapSize!==void 0&&(n.gapSize=this.gapSize),this.scale!==void 0&&(n.scale=this.scale),this.wireframe!==void 0&&(n.wireframe=this.wireframe),this.wireframeLinewidth!==void 0&&(n.wireframeLinewidth=this.wireframeLinewidth),this.wireframeLinecap!==void 0&&(n.wireframeLinecap=this.wireframeLinecap),this.wireframeLinejoin!==void 0&&(n.wireframeLinejoin=this.wireframeLinejoin),this.flatShading!==void 0&&(n.flatShading=this.flatShading),this.fog!==void 0&&(n.fog=this.fog),Object.keys(this.userData).length>0&&(n.userData=this.userData);function s(r){let a=[];for(let o in r){let c=r[o];delete c.metadata,a.push(c)}return a}if(t){let r=s(e.textures),a=s(e.images);r.length>0&&(n.textures=r),a.length>0&&(n.images=a)}return n}fromJSON(e,t){if(e.uuid!==void 0&&(this.uuid=e.uuid),e.name!==void 0&&(this.name=e.name),e.color!==void 0&&this.color!==void 0&&this.color.setHex(e.color),e.roughness!==void 0&&(this.roughness=e.roughness),e.metalness!==void 0&&(this.metalness=e.metalness),e.sheen!==void 0&&(this.sheen=e.sheen),e.sheenColor!==void 0&&(this.sheenColor=new Be().setHex(e.sheenColor)),e.sheenRoughness!==void 0&&(this.sheenRoughness=e.sheenRoughness),e.emissive!==void 0&&this.emissive!==void 0&&this.emissive.setHex(e.emissive),e.specular!==void 0&&this.specular!==void 0&&this.specular.setHex(e.specular),e.specularIntensity!==void 0&&(this.specularIntensity=e.specularIntensity),e.specularColor!==void 0&&this.specularColor!==void 0&&this.specularColor.setHex(e.specularColor),e.shininess!==void 0&&(this.shininess=e.shininess),e.clearcoat!==void 0&&(this.clearcoat=e.clearcoat),e.clearcoatRoughness!==void 0&&(this.clearcoatRoughness=e.clearcoatRoughness),e.dispersion!==void 0&&(this.dispersion=e.dispersion),e.retroreflectivity!==void 0&&(this.retroreflectivity=e.retroreflectivity),e.iridescence!==void 0&&(this.iridescence=e.iridescence),e.iridescenceIOR!==void 0&&(this.iridescenceIOR=e.iridescenceIOR),e.iridescenceThicknessRange!==void 0&&(this.iridescenceThicknessRange=e.iridescenceThicknessRange),e.transmission!==void 0&&(this.transmission=e.transmission),e.thickness!==void 0&&(this.thickness=e.thickness),e.attenuationDistance!==void 0&&(this.attenuationDistance=e.attenuationDistance),e.attenuationColor!==void 0&&this.attenuationColor!==void 0&&this.attenuationColor.setHex(e.attenuationColor),e.anisotropy!==void 0&&(this.anisotropy=e.anisotropy),e.anisotropyRotation!==void 0&&(this.anisotropyRotation=e.anisotropyRotation),e.fog!==void 0&&(this.fog=e.fog),e.flatShading!==void 0&&(this.flatShading=e.flatShading),e.blending!==void 0&&(this.blending=e.blending),e.combine!==void 0&&(this.combine=e.combine),e.side!==void 0&&(this.side=e.side),e.shadowSide!==void 0&&(this.shadowSide=e.shadowSide),e.opacity!==void 0&&(this.opacity=e.opacity),e.transparent!==void 0&&(this.transparent=e.transparent),e.alphaTest!==void 0&&(this.alphaTest=e.alphaTest),e.alphaHash!==void 0&&(this.alphaHash=e.alphaHash),e.depthFunc!==void 0&&(this.depthFunc=e.depthFunc),e.depthTest!==void 0&&(this.depthTest=e.depthTest),e.depthWrite!==void 0&&(this.depthWrite=e.depthWrite),e.colorWrite!==void 0&&(this.colorWrite=e.colorWrite),e.clippingPlanes!==void 0&&(this.clippingPlanes=e.clippingPlanes.map(n=>new Hn().fromJSON(n))),e.clipIntersection!==void 0&&(this.clipIntersection=e.clipIntersection),e.clipShadows!==void 0&&(this.clipShadows=e.clipShadows),e.depthPacking!==void 0&&(this.depthPacking=e.depthPacking),e.blendSrc!==void 0&&(this.blendSrc=e.blendSrc),e.blendDst!==void 0&&(this.blendDst=e.blendDst),e.blendEquation!==void 0&&(this.blendEquation=e.blendEquation),e.blendSrcAlpha!==void 0&&(this.blendSrcAlpha=e.blendSrcAlpha),e.blendDstAlpha!==void 0&&(this.blendDstAlpha=e.blendDstAlpha),e.blendEquationAlpha!==void 0&&(this.blendEquationAlpha=e.blendEquationAlpha),e.blendColor!==void 0&&this.blendColor!==void 0&&this.blendColor.setHex(e.blendColor),e.blendAlpha!==void 0&&(this.blendAlpha=e.blendAlpha),e.stencilWriteMask!==void 0&&(this.stencilWriteMask=e.stencilWriteMask),e.stencilFunc!==void 0&&(this.stencilFunc=e.stencilFunc),e.stencilRef!==void 0&&(this.stencilRef=e.stencilRef),e.stencilFuncMask!==void 0&&(this.stencilFuncMask=e.stencilFuncMask),e.stencilFail!==void 0&&(this.stencilFail=e.stencilFail),e.stencilZFail!==void 0&&(this.stencilZFail=e.stencilZFail),e.stencilZPass!==void 0&&(this.stencilZPass=e.stencilZPass),e.stencilWrite!==void 0&&(this.stencilWrite=e.stencilWrite),e.wireframe!==void 0&&(this.wireframe=e.wireframe),e.wireframeLinewidth!==void 0&&(this.wireframeLinewidth=e.wireframeLinewidth),e.wireframeLinecap!==void 0&&(this.wireframeLinecap=e.wireframeLinecap),e.wireframeLinejoin!==void 0&&(this.wireframeLinejoin=e.wireframeLinejoin),e.rotation!==void 0&&(this.rotation=e.rotation),e.linewidth!==void 0&&(this.linewidth=e.linewidth),e.linecap!==void 0&&(this.linecap=e.linecap),e.linejoin!==void 0&&(this.linejoin=e.linejoin),e.dashSize!==void 0&&(this.dashSize=e.dashSize),e.gapSize!==void 0&&(this.gapSize=e.gapSize),e.scale!==void 0&&(this.scale=e.scale),e.polygonOffset!==void 0&&(this.polygonOffset=e.polygonOffset),e.polygonOffsetFactor!==void 0&&(this.polygonOffsetFactor=e.polygonOffsetFactor),e.polygonOffsetUnits!==void 0&&(this.polygonOffsetUnits=e.polygonOffsetUnits),e.dithering!==void 0&&(this.dithering=e.dithering),e.alphaToCoverage!==void 0&&(this.alphaToCoverage=e.alphaToCoverage),e.premultipliedAlpha!==void 0&&(this.premultipliedAlpha=e.premultipliedAlpha),e.forceSinglePass!==void 0&&(this.forceSinglePass=e.forceSinglePass),e.allowOverride!==void 0&&(this.allowOverride=e.allowOverride),e.visible!==void 0&&(this.visible=e.visible),e.toneMapped!==void 0&&(this.toneMapped=e.toneMapped),e.userData!==void 0&&(this.userData=e.userData),e.vertexColors!==void 0&&(typeof e.vertexColors=="number"?this.vertexColors=e.vertexColors>0:this.vertexColors=e.vertexColors),e.size!==void 0&&(this.size=e.size),e.sizeAttenuation!==void 0&&(this.sizeAttenuation=e.sizeAttenuation),e.map!==void 0&&(this.map=t[e.map]||null),e.matcap!==void 0&&(this.matcap=t[e.matcap]||null),e.alphaMap!==void 0&&(this.alphaMap=t[e.alphaMap]||null),e.bumpMap!==void 0&&(this.bumpMap=t[e.bumpMap]||null),e.bumpScale!==void 0&&(this.bumpScale=e.bumpScale),e.normalMap!==void 0&&(this.normalMap=t[e.normalMap]||null),e.normalMapType!==void 0&&(this.normalMapType=e.normalMapType),e.normalScale!==void 0){let n=e.normalScale;Array.isArray(n)===!1&&(n=[n,n]),this.normalScale=new ae().fromArray(n)}return e.displacementMap!==void 0&&(this.displacementMap=t[e.displacementMap]||null),e.displacementScale!==void 0&&(this.displacementScale=e.displacementScale),e.displacementBias!==void 0&&(this.displacementBias=e.displacementBias),e.roughnessMap!==void 0&&(this.roughnessMap=t[e.roughnessMap]||null),e.metalnessMap!==void 0&&(this.metalnessMap=t[e.metalnessMap]||null),e.emissiveMap!==void 0&&(this.emissiveMap=t[e.emissiveMap]||null),e.emissiveIntensity!==void 0&&(this.emissiveIntensity=e.emissiveIntensity),e.specularMap!==void 0&&(this.specularMap=t[e.specularMap]||null),e.specularIntensityMap!==void 0&&(this.specularIntensityMap=t[e.specularIntensityMap]||null),e.specularColorMap!==void 0&&(this.specularColorMap=t[e.specularColorMap]||null),e.envMap!==void 0&&(this.envMap=t[e.envMap]||null),e.envMapRotation!==void 0&&this.envMapRotation.fromArray(e.envMapRotation),e.envMapIntensity!==void 0&&(this.envMapIntensity=e.envMapIntensity),e.reflectivity!==void 0&&(this.reflectivity=e.reflectivity),e.refractionRatio!==void 0&&(this.refractionRatio=e.refractionRatio),e.lightMap!==void 0&&(this.lightMap=t[e.lightMap]||null),e.lightMapIntensity!==void 0&&(this.lightMapIntensity=e.lightMapIntensity),e.aoMap!==void 0&&(this.aoMap=t[e.aoMap]||null),e.aoMapIntensity!==void 0&&(this.aoMapIntensity=e.aoMapIntensity),e.gradientMap!==void 0&&(this.gradientMap=t[e.gradientMap]||null),e.clearcoatMap!==void 0&&(this.clearcoatMap=t[e.clearcoatMap]||null),e.clearcoatRoughnessMap!==void 0&&(this.clearcoatRoughnessMap=t[e.clearcoatRoughnessMap]||null),e.clearcoatNormalMap!==void 0&&(this.clearcoatNormalMap=t[e.clearcoatNormalMap]||null),e.clearcoatNormalScale!==void 0&&(this.clearcoatNormalScale=new ae().fromArray(e.clearcoatNormalScale)),e.iridescenceMap!==void 0&&(this.iridescenceMap=t[e.iridescenceMap]||null),e.iridescenceThicknessMap!==void 0&&(this.iridescenceThicknessMap=t[e.iridescenceThicknessMap]||null),e.transmissionMap!==void 0&&(this.transmissionMap=t[e.transmissionMap]||null),e.thicknessMap!==void 0&&(this.thicknessMap=t[e.thicknessMap]||null),e.anisotropyMap!==void 0&&(this.anisotropyMap=t[e.anisotropyMap]||null),e.sheenColorMap!==void 0&&(this.sheenColorMap=t[e.sheenColorMap]||null),e.sheenRoughnessMap!==void 0&&(this.sheenRoughnessMap=t[e.sheenRoughnessMap]||null),this}clone(){return new this.constructor().copy(this)}copy(e){this.name=e.name,this.blending=e.blending,this.side=e.side,this.vertexColors=e.vertexColors,this.opacity=e.opacity,this.transparent=e.transparent,this.blendSrc=e.blendSrc,this.blendDst=e.blendDst,this.blendEquation=e.blendEquation,this.blendSrcAlpha=e.blendSrcAlpha,this.blendDstAlpha=e.blendDstAlpha,this.blendEquationAlpha=e.blendEquationAlpha,this.blendColor.copy(e.blendColor),this.blendAlpha=e.blendAlpha,this.depthFunc=e.depthFunc,this.depthTest=e.depthTest,this.depthWrite=e.depthWrite,this.stencilWriteMask=e.stencilWriteMask,this.stencilFunc=e.stencilFunc,this.stencilRef=e.stencilRef,this.stencilFuncMask=e.stencilFuncMask,this.stencilFail=e.stencilFail,this.stencilZFail=e.stencilZFail,this.stencilZPass=e.stencilZPass,this.stencilWrite=e.stencilWrite;let t=e.clippingPlanes,n=null;if(t!==null){let s=t.length;n=new Array(s);for(let r=0;r!==s;++r)n[r]=t[r].clone()}return this.clippingPlanes=n,this.clipIntersection=e.clipIntersection,this.clipShadows=e.clipShadows,this.shadowSide=e.shadowSide,this.colorWrite=e.colorWrite,this.precision=e.precision,this.polygonOffset=e.polygonOffset,this.polygonOffsetFactor=e.polygonOffsetFactor,this.polygonOffsetUnits=e.polygonOffsetUnits,this.dithering=e.dithering,this.alphaTest=e.alphaTest,this.alphaHash=e.alphaHash,this.alphaToCoverage=e.alphaToCoverage,this.premultipliedAlpha=e.premultipliedAlpha,this.forceSinglePass=e.forceSinglePass,this.allowOverride=e.allowOverride,this.visible=e.visible,this.toneMapped=e.toneMapped,this.userData=JSON.parse(JSON.stringify(e.userData)),this}dispose(){this.dispatchEvent({type:"dispose"})}set needsUpdate(e){e===!0&&this.version++}};var Ki=new P,_h=new P,Uo=new P,Oo=new P,Li=class{constructor(e=new P,t=new P(0,0,-1)){this.origin=e,this.direction=t}set(e,t){return this.origin.copy(e),this.direction.copy(t),this}copy(e){return this.origin.copy(e.origin),this.direction.copy(e.direction),this}at(e,t){return t.copy(this.origin).addScaledVector(this.direction,e)}lookAt(e){return this.direction.copy(e).sub(this.origin).normalize(),this}recast(e){return this.origin.copy(this.at(e,Ki)),this}closestPointToPoint(e,t){t.subVectors(e,this.origin);let n=t.dot(this.direction);return n<0?t.copy(this.origin):t.copy(this.origin).addScaledVector(this.direction,n)}distanceToPoint(e){return Math.sqrt(this.distanceSqToPoint(e))}distanceSqToPoint(e){let t=Ki.subVectors(e,this.origin).dot(this.direction);return t<0?this.origin.distanceToSquared(e):(Ki.copy(this.origin).addScaledVector(this.direction,t),Ki.distanceToSquared(e))}distanceSqToSegment(e,t,n,s){_h.copy(e).add(t).multiplyScalar(.5),Uo.copy(t).sub(e).normalize(),Oo.copy(this.origin).sub(_h);let r=e.distanceTo(t)*.5,a=-this.direction.dot(Uo),o=Oo.dot(this.direction),c=-Oo.dot(Uo),l=Oo.lengthSq(),h=Math.abs(1-a*a),u,d,f,m;if(h>0)if(u=a*c-o,d=a*o-c,m=r*h,u>=0)if(d>=-m)if(d<=m){let b=1/h;u*=b,d*=b,f=u*(u+a*d+2*o)+d*(a*u+d+2*c)+l}else d=r,u=Math.max(0,-(a*d+o)),f=-u*u+d*(d+2*c)+l;else d=-r,u=Math.max(0,-(a*d+o)),f=-u*u+d*(d+2*c)+l;else d<=-m?(u=Math.max(0,-(-a*r+o)),d=u>0?-r:Math.min(Math.max(-r,-c),r),f=-u*u+d*(d+2*c)+l):d<=m?(u=0,d=Math.min(Math.max(-r,-c),r),f=d*(d+2*c)+l):(u=Math.max(0,-(a*r+o)),d=u>0?r:Math.min(Math.max(-r,-c),r),f=-u*u+d*(d+2*c)+l);else d=a>0?-r:r,u=Math.max(0,-(a*d+o)),f=-u*u+d*(d+2*c)+l;return n&&n.copy(this.origin).addScaledVector(this.direction,u),s&&s.copy(_h).addScaledVector(Uo,d),f}intersectSphere(e,t){if(e.radius<0)return null;Ki.subVectors(e.center,this.origin);let n=Ki.dot(this.direction),s=Ki.dot(Ki)-n*n,r=e.radius*e.radius;if(s>r)return null;let a=Math.sqrt(r-s),o=n-a,c=n+a;return c<0?null:o<0?this.at(c,t):this.at(o,t)}intersectsSphere(e){return e.radius<0?!1:this.distanceSqToPoint(e.center)<=e.radius*e.radius}distanceToPlane(e){let t=e.normal.dot(this.direction);if(t===0)return e.distanceToPoint(this.origin)===0?0:null;let n=-(this.origin.dot(e.normal)+e.constant)/t;return n>=0?n:null}intersectPlane(e,t){let n=this.distanceToPlane(e);return n===null?null:this.at(n,t)}intersectsPlane(e){let t=e.distanceToPoint(this.origin);return t===0||e.normal.dot(this.direction)*t<0}intersectBox(e,t){let n,s,r,a,o,c,l=1/this.direction.x,h=1/this.direction.y,u=1/this.direction.z,d=this.origin;return l>=0?(n=(e.min.x-d.x)*l,s=(e.max.x-d.x)*l):(n=(e.max.x-d.x)*l,s=(e.min.x-d.x)*l),h>=0?(r=(e.min.y-d.y)*h,a=(e.max.y-d.y)*h):(r=(e.max.y-d.y)*h,a=(e.min.y-d.y)*h),n>a||r>s||((r>n||isNaN(n))&&(n=r),(a<s||isNaN(s))&&(s=a),u>=0?(o=(e.min.z-d.z)*u,c=(e.max.z-d.z)*u):(o=(e.max.z-d.z)*u,c=(e.min.z-d.z)*u),n>c||o>s)||((o>n||n!==n)&&(n=o),(c<s||s!==s)&&(s=c),s<0)?null:this.at(n>=0?n:s,t)}intersectsBox(e){return this.intersectBox(e,Ki)!==null}intersectTriangle(e,t,n,s,r){let a=this.origin,o=this.direction,c=o.x,l=o.y,h=o.z,u=e.x-a.x,d=e.y-a.y,f=e.z-a.z,m=t.x-a.x,b=t.y-a.y,g=t.z-a.z,p=n.x-a.x,_=n.y-a.y,S=n.z-a.z,x=Math.abs(c),M=Math.abs(l),E=Math.abs(h),A,y,w,R,I,L,F,D,k,W,X,re;if(x>=M&&x>=E?(w=c,L=u,k=m,re=p,c>=0?(A=l,y=h,R=d,I=f,F=b,D=g,W=_,X=S):(A=h,y=l,R=f,I=d,F=g,D=b,W=S,X=_)):M>=E?(w=l,L=d,k=b,re=_,l>=0?(A=h,y=c,R=f,I=u,F=g,D=m,W=S,X=p):(A=c,y=h,R=u,I=f,F=m,D=g,W=p,X=S)):(w=h,L=f,k=g,re=S,h>=0?(A=c,y=l,R=u,I=d,F=m,D=b,W=p,X=_):(A=l,y=c,R=d,I=u,F=b,D=m,W=_,X=p)),w===0)return null;let j=A/w,ee=y/w,te=1/w,Ne=R-j*L,Se=I-ee*L,ct=F-j*k,$e=D-ee*k,ht=W-j*re,J=X-ee*re,ie=ht*$e-J*ct,ve=Ne*J-Se*ht,Ke=ct*Se-$e*Ne;if(s){if(ie<0||ve<0||Ke<0)return null}else if((ie<0||ve<0||Ke<0)&&(ie>0||ve>0||Ke>0))return null;let Ae=ie+ve+Ke;if(Ae===0)return null;let ze=te*(ie*L+ve*k+Ke*re);return(Ae>0?ze<0:ze>0)?null:this.at(ze/Ae,r)}applyMatrix4(e){return this.origin.applyMatrix4(e),this.direction.transformDirection(e),this}equals(e){return e.origin.equals(this.origin)&&e.direction.equals(this.direction)}clone(){return new this.constructor().copy(this)}},jt=class extends Nn{constructor(e){super(),this.isMeshBasicMaterial=!0,this.type="MeshBasicMaterial",this.color=new Be(16777215),this.map=null,this.lightMap=null,this.lightMapIntensity=1,this.aoMap=null,this.aoMapIntensity=1,this.specularMap=null,this.alphaMap=null,this.envMap=null,this.envMapRotation=new si,this.combine=Uc,this.reflectivity=1,this.refractionRatio=.98,this.wireframe=!1,this.wireframeLinewidth=1,this.wireframeLinecap="round",this.wireframeLinejoin="round",this.fog=!0,this.setValues(e)}copy(e){return super.copy(e),this.color.copy(e.color),this.map=e.map,this.lightMap=e.lightMap,this.lightMapIntensity=e.lightMapIntensity,this.aoMap=e.aoMap,this.aoMapIntensity=e.aoMapIntensity,this.specularMap=e.specularMap,this.alphaMap=e.alphaMap,this.envMap=e.envMap,this.envMapRotation.copy(e.envMapRotation),this.combine=e.combine,this.reflectivity=e.reflectivity,this.refractionRatio=e.refractionRatio,this.wireframe=e.wireframe,this.wireframeLinewidth=e.wireframeLinewidth,this.wireframeLinecap=e.wireframeLinecap,this.wireframeLinejoin=e.wireframeLinejoin,this.fog=e.fog,this}},nf=new Ze,zs=new Li,Bo=new mn,sf=new P,ko=new P,zo=new P,Ho=new P,yh=new P,Go=new P,rf=new P,Vo=new P,ke=class extends At{constructor(e=new Bt,t=new jt){super(),this.isMesh=!0,this.type="Mesh",this.geometry=e,this.material=t,this.morphTargetDictionary=void 0,this.morphTargetInfluences=void 0,this.count=1,this.updateMorphTargets()}copy(e,t){return super.copy(e,t),e.morphTargetInfluences!==void 0&&(this.morphTargetInfluences=e.morphTargetInfluences.slice()),e.morphTargetDictionary!==void 0&&(this.morphTargetDictionary=Object.assign({},e.morphTargetDictionary)),this.material=Array.isArray(e.material)?e.material.slice():e.material,this.geometry=e.geometry,this}updateMorphTargets(){let t=this.geometry.morphAttributes,n=Object.keys(t);if(n.length>0){let s=t[n[0]];if(s!==void 0){this.morphTargetInfluences=[],this.morphTargetDictionary={};for(let r=0,a=s.length;r<a;r++){let o=s[r].name||String(r);this.morphTargetInfluences.push(0),this.morphTargetDictionary[o]=r}}}}getVertexPosition(e,t){let n=this.geometry,s=n.attributes.position,r=n.morphAttributes.position,a=n.morphTargetsRelative;t.fromBufferAttribute(s,e);let o=this.morphTargetInfluences;if(r&&o){Go.set(0,0,0);for(let c=0,l=r.length;c<l;c++){let h=o[c],u=r[c];h!==0&&(yh.fromBufferAttribute(u,e),a?Go.addScaledVector(yh,h):Go.addScaledVector(yh.sub(t),h))}t.add(Go)}return t}intersectsFrustum(e){return e.intersectsObject(this)}raycast(e,t){let n=this.geometry,s=this.material,r=this.matrixWorld;s!==void 0&&(n.boundingSphere===null&&n.computeBoundingSphere(),Bo.copy(n.boundingSphere),Bo.applyMatrix4(r),zs.copy(e.ray).recast(e.near),!(Bo.containsPoint(zs.origin)===!1&&(zs.intersectSphere(Bo,sf)===null||zs.origin.distanceToSquared(sf)>(e.far-e.near)**2))&&(nf.copy(r).invert(),zs.copy(e.ray).applyMatrix4(nf),!(n.boundingBox!==null&&zs.intersectsBox(n.boundingBox)===!1)&&this._computeIntersections(e,t,zs)))}_computeIntersections(e,t,n){let s,r=this.geometry,a=this.material,o=r.index,c=r.attributes.position,l=r.attributes.uv,h=r.attributes.uv1,u=r.attributes.normal,d=r.groups,f=r.drawRange;if(o!==null)if(Array.isArray(a))for(let m=0,b=d.length;m<b;m++){let g=d[m],p=a[g.materialIndex],_=Math.max(g.start,f.start),S=Math.min(o.count,Math.min(g.start+g.count,f.start+f.count));for(let x=_,M=S;x<M;x+=3){let E=o.getX(x),A=o.getX(x+1),y=o.getX(x+2);s=Wo(this,p,e,n,l,h,u,E,A,y),s&&(s.faceIndex=Math.floor(x/3),s.face.materialIndex=g.materialIndex,t.push(s))}}else{let m=Math.max(0,f.start),b=Math.min(o.count,f.start+f.count);for(let g=m,p=b;g<p;g+=3){let _=o.getX(g),S=o.getX(g+1),x=o.getX(g+2);s=Wo(this,a,e,n,l,h,u,_,S,x),s&&(s.faceIndex=Math.floor(g/3),t.push(s))}}else if(c!==void 0)if(Array.isArray(a))for(let m=0,b=d.length;m<b;m++){let g=d[m],p=a[g.materialIndex],_=Math.max(g.start,f.start),S=Math.min(c.count,Math.min(g.start+g.count,f.start+f.count));for(let x=_,M=S;x<M;x+=3){let E=x,A=x+1,y=x+2;s=Wo(this,p,e,n,l,h,u,E,A,y),s&&(s.faceIndex=Math.floor(x/3),s.face.materialIndex=g.materialIndex,t.push(s))}}else{let m=Math.max(0,f.start),b=Math.min(c.count,f.start+f.count);for(let g=m,p=b;g<p;g+=3){let _=g,S=g+1,x=g+2;s=Wo(this,a,e,n,l,h,u,_,S,x),s&&(s.faceIndex=Math.floor(g/3),t.push(s))}}}};function Pm(i,e,t,n,s,r,a,o){let c;if(e.side===yn?c=n.intersectTriangle(a,r,s,!0,o):c=n.intersectTriangle(s,r,a,e.side===Ui,o),c===null)return null;Vo.copy(o),Vo.applyMatrix4(i.matrixWorld);let l=t.ray.origin.distanceTo(Vo);return l<t.near||l>t.far?null:{distance:l,point:Vo.clone(),object:i}}function Wo(i,e,t,n,s,r,a,o,c,l){i.getVertexPosition(o,ko),i.getVertexPosition(c,zo),i.getVertexPosition(l,Ho);let h=Pm(i,e,t,n,ko,zo,Ho,rf);if(h){let u=new P;xs.getBarycoord(rf,ko,zo,Ho,u),s&&(h.uv=xs.getInterpolatedAttribute(s,o,c,l,u,new ae)),r&&(h.uv1=xs.getInterpolatedAttribute(r,o,c,l,u,new ae)),a&&(h.normal=xs.getInterpolatedAttribute(a,o,c,l,u,new P),h.normal.dot(n.direction)>0&&h.normal.multiplyScalar(-1));let d={a:o,b:c,c:l,normal:new P,materialIndex:0};xs.getNormal(ko,zo,Ho,d.normal),h.face=d,h.barycoord=u}return h}var ma=new Pt,af=new Pt,of=new Pt,Im=new Pt,cf=new Ze,qo=new P,vh=new mn,lf=new Ze,Mh=new Li,Ca=class extends ke{constructor(e,t){super(e,t),this.isSkinnedMesh=!0,this.type="SkinnedMesh",this.bindMode=Ch,this.bindMatrix=new Ze,this.bindMatrixInverse=new Ze,this.boundingBox=null,this.boundingSphere=null}computeBoundingBox(){let e=this.geometry;this.boundingBox===null&&(this.boundingBox=new en),this.boundingBox.makeEmpty();let t=e.getAttribute("position");for(let n=0;n<t.count;n++)this.getVertexPosition(n,qo),this.boundingBox.expandByPoint(qo)}computeBoundingSphere(){let e=this.geometry;this.boundingSphere===null&&(this.boundingSphere=new mn),this.boundingSphere.makeEmpty();let t=e.getAttribute("position");for(let n=0;n<t.count;n++)this.getVertexPosition(n,qo),this.boundingSphere.expandByPoint(qo)}copy(e,t){return super.copy(e,t),this.bindMode=e.bindMode,this.bindMatrix.copy(e.bindMatrix),this.bindMatrixInverse.copy(e.bindMatrixInverse),this.skeleton=e.skeleton,e.boundingBox!==null&&(this.boundingBox=e.boundingBox.clone()),e.boundingSphere!==null&&(this.boundingSphere=e.boundingSphere.clone()),this}raycast(e,t){let n=this.material,s=this.matrixWorld;n!==void 0&&(this.boundingSphere===null&&this.computeBoundingSphere(),vh.copy(this.boundingSphere),vh.applyMatrix4(s),e.ray.intersectsSphere(vh)!==!1&&(lf.copy(s).invert(),Mh.copy(e.ray).applyMatrix4(lf),!(this.boundingBox!==null&&Mh.intersectsBox(this.boundingBox)===!1)&&this._computeIntersections(e,t,Mh)))}getVertexPosition(e,t){return super.getVertexPosition(e,t),this.applyBoneTransform(e,t),t}bind(e,t){this.skeleton=e,t===void 0&&(this.updateMatrixWorld(!0),this.skeleton.calculateInverses(),t=this.matrixWorld),this.bindMatrix.copy(t),this.bindMatrixInverse.copy(t).invert()}pose(){this.skeleton.pose()}normalizeSkinWeights(){let e=new Pt,t=this.geometry.attributes.skinWeight;for(let n=0,s=t.count;n<s;n++){e.fromBufferAttribute(t,n);let r=1/e.manhattanLength();r!==1/0?e.multiplyScalar(r):e.set(1,0,0,0),t.setXYZW(n,e.x,e.y,e.z,e.w)}}updateMatrixWorld(e){super.updateMatrixWorld(e),this.bindMode===Ch?this.bindMatrixInverse.copy(this.matrixWorld).invert():this.bindMode===ip?this.bindMatrixInverse.copy(this.bindMatrix).invert():Ve("SkinnedMesh: Unrecognized bindMode: "+this.bindMode)}applyBoneTransform(e,t){let n=this.skeleton,s=this.geometry;af.fromBufferAttribute(s.attributes.skinIndex,e),of.fromBufferAttribute(s.attributes.skinWeight,e),t.isVector4?(ma.copy(t),t.set(0,0,0,0)):(ma.set(...t,1),t.set(0,0,0)),ma.applyMatrix4(this.bindMatrix);for(let r=0;r<4;r++){let a=of.getComponent(r);if(a!==0){let o=af.getComponent(r);cf.multiplyMatrices(n.bones[o].matrixWorld,n.boneInverses[o]),t.addScaledVector(Im.copy(ma).applyMatrix4(cf),a)}}return t.isVector4&&(t.w=ma.w),t.applyMatrix4(this.bindMatrixInverse)}},Or=class extends At{constructor(){super(),this.isBone=!0,this.type="Bone"}},Br=class extends hn{constructor(e=null,t=1,n=1,s,r,a,o,c,l=Wt,h=Wt,u,d){super(null,a,o,c,l,h,s,r,u,d),this.isDataTexture=!0,this.image={data:e,width:t,height:n},this.generateMipmaps=!1,this.flipY=!1,this.unpackAlignment=1}},hf=new Ze,Lm=new Ze,Pa=class i{constructor(e=[],t=[]){this.uuid=ii(),this.bones=e.slice(0),this.boneInverses=t,this.boneMatrices=null,this.boneTexture=null,this.init()}init(){let e=this.bones,t=this.boneInverses;if(this.boneMatrices=new Float32Array(e.length*16),t.length===0)this.calculateInverses();else if(e.length!==t.length){Ve("Skeleton: Number of inverse bone matrices does not match amount of bones."),this.boneInverses=[];for(let n=0,s=this.bones.length;n<s;n++)this.boneInverses.push(new Ze)}}calculateInverses(){this.boneInverses.length=0;for(let e=0,t=this.bones.length;e<t;e++){let n=new Ze;this.bones[e]&&n.copy(this.bones[e].matrixWorld).invert(),this.boneInverses.push(n)}}pose(){for(let e=0,t=this.bones.length;e<t;e++){let n=this.bones[e];n&&n.matrixWorld.copy(this.boneInverses[e]).invert()}for(let e=0,t=this.bones.length;e<t;e++){let n=this.bones[e];n&&(n.parent&&n.parent.isBone?(n.matrix.copy(n.parent.matrixWorld).invert(),n.matrix.multiply(n.matrixWorld)):n.matrix.copy(n.matrixWorld),n.matrix.decompose(n.position,n.quaternion,n.scale))}}update(){let e=this.bones,t=this.boneInverses,n=this.boneMatrices,s=this.boneTexture;for(let r=0,a=e.length;r<a;r++){let o=e[r]?e[r].matrixWorld:Lm;hf.multiplyMatrices(o,t[r]),hf.toArray(n,r*16)}s!==null&&(s.needsUpdate=!0)}clone(){return new i(this.bones,this.boneInverses)}computeBoneTexture(){let e=Math.sqrt(this.bones.length*4);e=Math.ceil(e/4)*4,e=Math.max(e,4);let t=new Float32Array(e*e*4);t.set(this.boneMatrices);let n=new Br(t,e,e,jn,Xn);return n.needsUpdate=!0,this.boneMatrices=t,this.boneTexture=n,this}getBoneByName(e){for(let t=0,n=this.bones.length;t<n;t++){let s=this.bones[t];if(s.name===e)return s}}dispose(){this.boneTexture!==null&&(this.boneTexture.dispose(),this.boneTexture=null)}fromJSON(e,t){this.uuid=e.uuid;for(let n=0,s=e.bones.length;n<s;n++){let r=e.bones[n],a=t[r];a===void 0&&(Ve("Skeleton: No bone found with UUID:",r),a=new Or),this.bones.push(a),this.boneInverses.push(new Ze().fromArray(e.boneInverses[n]))}return this.init(),this}toJSON(){let e={metadata:{version:4.7,type:"Skeleton",generator:"Skeleton.toJSON"},bones:[],boneInverses:[]};e.uuid=this.uuid;let t=this.bones,n=this.boneInverses;for(let s=0,r=t.length;s<r;s++){let a=t[s];e.bones.push(a.uuid);let o=n[s];e.boneInverses.push(o.toArray())}return e}},Zi=class extends an{constructor(e,t,n,s=1){super(e,t,n),this.isInstancedBufferAttribute=!0,this.meshPerAttribute=s}copy(e){return super.copy(e),this.meshPerAttribute=e.meshPerAttribute,this}toJSON(){let e=super.toJSON();return e.meshPerAttribute=this.meshPerAttribute,e.isInstancedBufferAttribute=!0,e}},Mr=new Ze,uf=new Ze,Xo=[],df=new en,Dm=new Ze,ga=new ke,ba=new mn,ri=class extends ke{constructor(e,t,n){super(e,t),this.isInstancedMesh=!0,this.instanceMatrix=new Zi(new Float32Array(n*16),16),this.instanceColor=null,this.morphTexture=null,this.count=n,this.boundingBox=null,this.boundingSphere=null;for(let s=0;s<n;s++)this.setMatrixAt(s,Dm)}computeBoundingBox(){let e=this.geometry,t=this.count;this.boundingBox===null&&(this.boundingBox=new en),e.boundingBox===null&&e.computeBoundingBox(),this.boundingBox.makeEmpty();for(let n=0;n<t;n++)this.getMatrixAt(n,Mr),df.copy(e.boundingBox).applyMatrix4(Mr),this.boundingBox.union(df)}computeBoundingSphere(){let e=this.geometry,t=this.count;this.boundingSphere===null&&(this.boundingSphere=new mn),e.boundingSphere===null&&e.computeBoundingSphere(),this.boundingSphere.makeEmpty();for(let n=0;n<t;n++)this.getMatrixAt(n,Mr),ba.copy(e.boundingSphere).applyMatrix4(Mr),this.boundingSphere.union(ba)}copy(e,t){return super.copy(e,t),this.instanceMatrix.copy(e.instanceMatrix),e.morphTexture!==null&&(this.morphTexture=e.morphTexture.clone()),e.instanceColor!==null&&(this.instanceColor=e.instanceColor.clone()),this.count=e.count,e.boundingBox!==null&&(this.boundingBox=e.boundingBox.clone()),e.boundingSphere!==null&&(this.boundingSphere=e.boundingSphere.clone()),this}getColorAt(e,t){return this.instanceColor===null?t.setRGB(1,1,1):t.fromArray(this.instanceColor.array,e*3)}getMatrixAt(e,t){return t.fromArray(this.instanceMatrix.array,e*16)}getMorphAt(e,t){let n=t.morphTargetInfluences,s=this.morphTexture.source.data.data,r=n.length+1,a=e*r+1;for(let o=0;o<n.length;o++)n[o]=s[a+o]}raycast(e,t){let n=this.matrixWorld,s=this.count;if(ga.geometry=this.geometry,ga.material=this.material,ga.material!==void 0&&(this.boundingSphere===null&&this.computeBoundingSphere(),ba.copy(this.boundingSphere),ba.applyMatrix4(n),e.ray.intersectsSphere(ba)!==!1))for(let r=0;r<s;r++){this.getMatrixAt(r,Mr),uf.multiplyMatrices(n,Mr),ga.matrixWorld=uf,ga.raycast(e,Xo);for(let a=0,o=Xo.length;a<o;a++){let c=Xo[a];c.instanceId=r,c.object=this,t.push(c)}Xo.length=0}}setColorAt(e,t){return this.instanceColor===null&&(this.instanceColor=new Zi(new Float32Array(this.instanceMatrix.count*3).fill(1),3)),t.toArray(this.instanceColor.array,e*3),this}setMatrixAt(e,t){return t.toArray(this.instanceMatrix.array,e*16),this}setMorphAt(e,t){let n=t.morphTargetInfluences,s=n.length+1;this.morphTexture===null&&(this.morphTexture=new Br(new Float32Array(s*this.count),s,this.count,Vc,Xn));let r=this.morphTexture.source.data.data,a=0;for(let l=0;l<n.length;l++)a+=n[l];let o=this.geometry.morphTargetsRelative?1:1-a,c=s*e;return r[c]=o,r.set(n,c+1),this}updateMorphTargets(){}dispose(){super.dispose(),this.morphTexture!==null&&(this.morphTexture.dispose(),this.morphTexture=null)}},Hs=new mn,Nm=new ae(.5,.5),jo=new P,kr=class{constructor(e=new Hn,t=new Hn,n=new Hn,s=new Hn,r=new Hn,a=new Hn){this.planes=[e,t,n,s,r,a]}set(e,t,n,s,r,a){let o=this.planes;return o[0].copy(e),o[1].copy(t),o[2].copy(n),o[3].copy(s),o[4].copy(r),o[5].copy(a),this}copy(e){let t=this.planes;for(let n=0;n<6;n++)t[n].copy(e.planes[n]);return this}setFromProjectionMatrix(e,t=xi,n=!1){let s=this.planes,r=e.elements,a=r[0],o=r[1],c=r[2],l=r[3],h=r[4],u=r[5],d=r[6],f=r[7],m=r[8],b=r[9],g=r[10],p=r[11],_=r[12],S=r[13],x=r[14],M=r[15];if(s[0].setComponents(l-a,f-h,p-m,M-_).normalize(),s[1].setComponents(l+a,f+h,p+m,M+_).normalize(),s[2].setComponents(l+o,f+u,p+b,M+S).normalize(),s[3].setComponents(l-o,f-u,p-b,M-S).normalize(),n)s[4].setComponents(c,d,g,x).normalize(),s[5].setComponents(l-c,f-d,p-g,M-x).normalize();else if(s[4].setComponents(l-c,f-d,p-g,M-x).normalize(),t===xi)s[5].setComponents(l+c,f+d,p+g,M+x).normalize();else if(t===Cr)s[5].setComponents(c,d,g,x).normalize();else throw new Error("THREE.Frustum.setFromProjectionMatrix(): Invalid coordinate system: "+t);return this}intersectsObject(e){if(e.boundingSphere!==void 0)e.boundingSphere===null&&e.computeBoundingSphere(),Hs.copy(e.boundingSphere).applyMatrix4(e.matrixWorld);else{let t=e.geometry;t.boundingSphere===null&&t.computeBoundingSphere(),Hs.copy(t.boundingSphere).applyMatrix4(e.matrixWorld)}return this.intersectsSphere(Hs)}intersectsSprite(e){Hs.center.set(0,0,0);let t=Nm.distanceTo(e.center);return Hs.radius=.7071067811865476+t,Hs.applyMatrix4(e.matrixWorld),this.intersectsSphere(Hs)}intersectsSphere(e){let t=this.planes,n=e.center,s=-e.radius;for(let r=0;r<6;r++)if(t[r].distanceToPoint(n)<s)return!1;return!0}intersectsBox(e){let t=this.planes;for(let n=0;n<6;n++){let s=t[n];if(jo.x=s.normal.x>0?e.max.x:e.min.x,jo.y=s.normal.y>0?e.max.y:e.min.y,jo.z=s.normal.z>0?e.max.z:e.min.z,s.distanceToPoint(jo)<0)return!1}return!0}containsPoint(e){let t=this.planes;for(let n=0;n<6;n++)if(t[n].distanceToPoint(e)<0)return!1;return!0}clone(){return new this.constructor().copy(this)}};var $i=class extends Nn{constructor(e){super(),this.isLineBasicMaterial=!0,this.type="LineBasicMaterial",this.color=new Be(16777215),this.map=null,this.linewidth=1,this.linecap="round",this.linejoin="round",this.fog=!0,this.setValues(e)}copy(e){return super.copy(e),this.color.copy(e.color),this.map=e.map,this.linewidth=e.linewidth,this.linecap=e.linecap,this.linejoin=e.linejoin,this.fog=e.fog,this}},mc=new P,gc=new P,ff=new Ze,xa=new Li,Ko=new mn,Sh=new P,pf=new P,js=class extends At{constructor(e=new Bt,t=new $i){super(),this.isLine=!0,this.type="Line",this.geometry=e,this.material=t,this.morphTargetDictionary=void 0,this.morphTargetInfluences=void 0,this.updateMorphTargets()}copy(e,t){return super.copy(e,t),this.material=Array.isArray(e.material)?e.material.slice():e.material,this.geometry=e.geometry,this}computeLineDistances(){let e=this.geometry;if(e.index===null){let t=e.attributes.position,n=[0];for(let s=1,r=t.count;s<r;s++)mc.fromBufferAttribute(t,s-1),gc.fromBufferAttribute(t,s),n[s]=n[s-1],n[s]+=mc.distanceTo(gc);e.setAttribute("lineDistance",new nt(n,1))}else Ve("Line.computeLineDistances(): Computation only possible with non-indexed BufferGeometry.");return this}intersectsFrustum(e){return e.intersectsObject(this)}raycast(e,t){let n=this.geometry,s=this.matrixWorld,r=e.params.Line.threshold,a=n.drawRange;if(n.boundingSphere===null&&n.computeBoundingSphere(),Ko.copy(n.boundingSphere),Ko.applyMatrix4(s),Ko.radius+=r,e.ray.intersectsSphere(Ko)===!1)return;ff.copy(s).invert(),xa.copy(e.ray).applyMatrix4(ff);let o=r/((this.scale.x+this.scale.y+this.scale.z)/3),c=o*o,l=this.isLineSegments?2:1,h=n.index,d=n.attributes.position;if(h!==null){let f=Math.max(0,a.start),m=Math.min(h.count,a.start+a.count);for(let b=f,g=m-1;b<g;b+=l){let p=h.getX(b),_=h.getX(b+1),S=Yo(this,e,xa,c,p,_,b);S&&t.push(S)}if(this.isLineLoop){let b=h.getX(m-1),g=h.getX(f),p=Yo(this,e,xa,c,b,g,m-1);p&&t.push(p)}}else{let f=Math.max(0,a.start),m=Math.min(d.count,a.start+a.count);for(let b=f,g=m-1;b<g;b+=l){let p=Yo(this,e,xa,c,b,b+1,b);p&&t.push(p)}if(this.isLineLoop){let b=Yo(this,e,xa,c,m-1,f,m-1);b&&t.push(b)}}}updateMorphTargets(){let t=this.geometry.morphAttributes,n=Object.keys(t);if(n.length>0){let s=t[n[0]];if(s!==void 0){this.morphTargetInfluences=[],this.morphTargetDictionary={};for(let r=0,a=s.length;r<a;r++){let o=s[r].name||String(r);this.morphTargetInfluences.push(0),this.morphTargetDictionary[o]=r}}}}};function Yo(i,e,t,n,s,r,a){let o=i.geometry.attributes.position;if(mc.fromBufferAttribute(o,s),gc.fromBufferAttribute(o,r),t.distanceSqToSegment(mc,gc,Sh,pf)>n)return;Sh.applyMatrix4(i.matrixWorld);let l=e.ray.origin.distanceTo(Sh);if(!(l<e.near||l>e.far))return{distance:l,point:pf.clone().applyMatrix4(i.matrixWorld),index:a,face:null,faceIndex:null,barycoord:null,object:i}}var mf=new P,gf=new P,Ia=class extends js{constructor(e,t){super(e,t),this.isLineSegments=!0,this.type="LineSegments"}computeLineDistances(){let e=this.geometry;if(e.index===null){let t=e.attributes.position,n=[];for(let s=0,r=t.count;s<r;s+=2)mf.fromBufferAttribute(t,s),gf.fromBufferAttribute(t,s+1),n[s]=s===0?0:n[s-1],n[s+1]=n[s]+mf.distanceTo(gf);e.setAttribute("lineDistance",new nt(n,1))}else Ve("LineSegments.computeLineDistances(): Computation only possible with non-indexed BufferGeometry.");return this}},La=class extends js{constructor(e,t){super(e,t),this.isLineLoop=!0,this.type="LineLoop"}},zr=class extends Nn{constructor(e){super(),this.isPointsMaterial=!0,this.type="PointsMaterial",this.color=new Be(16777215),this.map=null,this.alphaMap=null,this.size=1,this.sizeAttenuation=!0,this.fog=!0,this.setValues(e)}copy(e){return super.copy(e),this.color.copy(e.color),this.map=e.map,this.alphaMap=e.alphaMap,this.size=e.size,this.sizeAttenuation=e.sizeAttenuation,this.fog=e.fog,this}},bf=new Ze,Nh=new Li,Jo=new mn,Zo=new P,Da=class extends At{constructor(e=new Bt,t=new zr){super(),this.isPoints=!0,this.type="Points",this.geometry=e,this.material=t,this.morphTargetDictionary=void 0,this.morphTargetInfluences=void 0,this.updateMorphTargets()}copy(e,t){return super.copy(e,t),this.material=Array.isArray(e.material)?e.material.slice():e.material,this.geometry=e.geometry,this}intersectsFrustum(e){return e.intersectsObject(this)}raycast(e,t){let n=this.geometry,s=this.matrixWorld,r=e.params.Points.threshold,a=n.drawRange;if(n.boundingSphere===null&&n.computeBoundingSphere(),Jo.copy(n.boundingSphere),Jo.applyMatrix4(s),Jo.radius+=r,e.ray.intersectsSphere(Jo)===!1)return;bf.copy(s).invert(),Nh.copy(e.ray).applyMatrix4(bf);let o=r/((this.scale.x+this.scale.y+this.scale.z)/3),c=o*o,l=n.index,u=n.attributes.position;if(l!==null){let d=Math.max(0,a.start),f=Math.min(l.count,a.start+a.count);for(let m=d,b=f;m<b;m++){let g=l.getX(m);Zo.fromBufferAttribute(u,g),xf(Zo,g,c,s,e,t,this)}}else{let d=Math.max(0,a.start),f=Math.min(u.count,a.start+a.count);for(let m=d,b=f;m<b;m++)Zo.fromBufferAttribute(u,m),xf(Zo,m,c,s,e,t,this)}}updateMorphTargets(){let t=this.geometry.morphAttributes,n=Object.keys(t);if(n.length>0){let s=t[n[0]];if(s!==void 0){this.morphTargetInfluences=[],this.morphTargetDictionary={};for(let r=0,a=s.length;r<a;r++){let o=s[r].name||String(r);this.morphTargetInfluences.push(0),this.morphTargetDictionary[o]=r}}}}};function xf(i,e,t,n,s,r,a){let o=Nh.distanceSqToPoint(i);if(o<t){let c=new P;Nh.closestPointToPoint(i,c),c.applyMatrix4(n);let l=s.ray.origin.distanceTo(c);if(l<s.near||l>s.far)return;r.push({distance:l,distanceToRay:Math.sqrt(o),point:c,index:e,face:null,faceIndex:null,barycoord:null,object:a})}}var Na=class extends hn{constructor(e=[],t=Rs,n,s,r,a,o,c,l,h){super(e,t,n,s,r,a,o,c,l,h),this.isCubeTexture=!0,this.flipY=!1}get images(){return this.image}set images(e){this.image=e}},xn=class extends hn{constructor(e,t,n,s,r,a,o,c,l){super(e,t,n,s,r,a,o,c,l),this.isCanvasTexture=!0,this.needsUpdate=!0}};var _s=class extends hn{constructor(e,t,n=Mi,s,r,a,o=Wt,c=Wt,l,h=Ii,u=1){if(h!==Ii&&h!==Cs)throw new Error("THREE.DepthTexture: format must be either THREE.DepthFormat or THREE.DepthStencilFormat");let d={width:e,height:t,depth:u};super(d,s,r,a,o,c,h,n,l),this.isDepthTexture=!0,this.flipY=!1,this.generateMipmaps=!1,this.compareFunction=null}copy(e){return super.copy(e),this.source=new Lr(Object.assign({},e.image)),this.compareFunction=e.compareFunction,this}toJSON(e){let t=super.toJSON(e);return t.compareFunction=this.compareFunction,t}},bc=class extends _s{constructor(e,t=Mi,n=Rs,s,r,a=Wt,o=Wt,c,l=Ii){let h={width:e,height:e,depth:1},u=[h,h,h,h,h,h];super(e,e,t,n,s,r,a,o,c,l),this.image=u,this.isCubeDepthTexture=!0,this.isCubeTexture=!0}get images(){return this.image}set images(e){this.image=e}},Fa=class extends hn{constructor(e=null){super(),this.sourceTexture=e,this.isExternalTexture=!0}copy(e){return super.copy(e),this.sourceTexture=e.sourceTexture,this}},tn=class i extends Bt{constructor(e=1,t=1,n=1,s=1,r=1,a=1){super(),this.type="BoxGeometry",this.parameters={width:e,height:t,depth:n,widthSegments:s,heightSegments:r,depthSegments:a};let o=this;s=Math.floor(s),r=Math.floor(r),a=Math.floor(a);let c=[],l=[],h=[],u=[],d=0,f=0;m("z","y","x",-1,-1,n,t,e,a,r,0),m("z","y","x",1,-1,n,t,-e,a,r,1),m("x","z","y",1,1,e,n,t,s,a,2),m("x","z","y",1,-1,e,n,-t,s,a,3),m("x","y","z",1,-1,e,t,n,s,r,4),m("x","y","z",-1,-1,e,t,-n,s,r,5),this.setIndex(c),this.setAttribute("position",new nt(l,3)),this.setAttribute("normal",new nt(h,3)),this.setAttribute("uv",new nt(u,2));function m(b,g,p,_,S,x,M,E,A,y,w){let R=x/A,I=M/y,L=x/2,F=M/2,D=E/2,k=A+1,W=y+1,X=0,re=0,j=new P;for(let ee=0;ee<W;ee++){let te=ee*I-F;for(let Ne=0;Ne<k;Ne++){let Se=Ne*R-L;j[b]=Se*_,j[g]=te*S,j[p]=D,l.push(j.x,j.y,j.z),j[b]=0,j[g]=0,j[p]=E>0?1:-1,h.push(j.x,j.y,j.z),u.push(Ne/A),u.push(1-ee/y),X+=1}}for(let ee=0;ee<y;ee++)for(let te=0;te<A;te++){let Ne=d+te+k*ee,Se=d+te+k*(ee+1),ct=d+(te+1)+k*(ee+1),$e=d+(te+1)+k*ee;c.push(Ne,Se,$e),c.push(Se,ct,$e),re+=6}o.addGroup(f,re,w),f+=re,d+=X}}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}static fromJSON(e){return new i(e.width,e.height,e.depth,e.widthSegments,e.heightSegments,e.depthSegments)}};var Ks=class i extends Bt{constructor(e=1,t=32,n=0,s=Math.PI*2){super(),this.type="CircleGeometry",this.parameters={radius:e,segments:t,thetaStart:n,thetaLength:s},t=Math.max(3,t);let r=[],a=[],o=[],c=[],l=new P,h=new ae;a.push(0,0,0),o.push(0,0,1),c.push(.5,.5);for(let u=0,d=3;u<=t;u++,d+=3){let f=n+u/t*s;l.x=e*Math.cos(f),l.y=e*Math.sin(f),a.push(l.x,l.y,l.z),o.push(0,0,1),h.x=(a[d]/e+1)/2,h.y=(a[d+1]/e+1)/2,c.push(h.x,h.y)}for(let u=1;u<=t;u++)r.push(u,u+1,0);this.setIndex(r),this.setAttribute("position",new nt(a,3)),this.setAttribute("normal",new nt(o,3)),this.setAttribute("uv",new nt(c,2))}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}static fromJSON(e){return new i(e.radius,e.segments,e.thetaStart,e.thetaLength)}},Ua=class i extends Bt{constructor(e=1,t=1,n=1,s=32,r=1,a=!1,o=0,c=Math.PI*2){super(),this.type="CylinderGeometry",this.parameters={radiusTop:e,radiusBottom:t,height:n,radialSegments:s,heightSegments:r,openEnded:a,thetaStart:o,thetaLength:c};let l=this;s=Math.floor(s),r=Math.floor(r);let h=[],u=[],d=[],f=[],m=0,b=[],g=n/2,p=0;_(),a===!1&&(e>0&&S(!0),t>0&&S(!1)),this.setIndex(h),this.setAttribute("position",new nt(u,3)),this.setAttribute("normal",new nt(d,3)),this.setAttribute("uv",new nt(f,2));function _(){let x=new P,M=new P,E=0,A=(t-e)/n;for(let y=0;y<=r;y++){let w=[],R=y/r,I=R*(t-e)+e;for(let L=0;L<=s;L++){let F=L/s,D=F*c+o,k=Math.sin(D),W=Math.cos(D);M.x=I*k,M.y=-R*n+g,M.z=I*W,u.push(M.x,M.y,M.z),x.set(k,A,W).normalize(),d.push(x.x,x.y,x.z),f.push(F,1-R),w.push(m++)}b.push(w)}for(let y=0;y<s;y++)for(let w=0;w<r;w++){let R=b[w][y],I=b[w+1][y],L=b[w+1][y+1],F=b[w][y+1];(e>0||w!==0)&&(h.push(R,I,F),E+=3),(t>0||w!==r-1)&&(h.push(I,L,F),E+=3)}l.addGroup(p,E,0),p+=E}function S(x){let M=m,E=new ae,A=new P,y=0,w=x===!0?e:t,R=x===!0?1:-1;for(let L=1;L<=s;L++)u.push(0,g*R,0),d.push(0,R,0),f.push(.5,.5),m++;let I=m;for(let L=0;L<=s;L++){let D=L/s*c+o,k=Math.cos(D),W=Math.sin(D);A.x=w*W,A.y=g*R,A.z=w*k,u.push(A.x,A.y,A.z),d.push(0,R,0),E.x=k*.5+.5,E.y=W*.5*R+.5,f.push(E.x,E.y),m++}for(let L=0;L<s;L++){let F=M+L,D=I+L;x===!0?h.push(D,D+1,F):h.push(D+1,D,F),y+=3}l.addGroup(p,y,x===!0?1:2),p+=y}}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}static fromJSON(e){return new i(e.radiusTop,e.radiusBottom,e.height,e.radialSegments,e.heightSegments,e.openEnded,e.thetaStart,e.thetaLength)}};var Gn=class{constructor(){this.type="Curve",this.arcLengthDivisions=200,this.needsUpdate=!1,this.cacheArcLengths=null}getPoint(){Ve("Curve: .getPoint() not implemented.")}getPointAt(e,t){let n=this.getUtoTmapping(e);return this.getPoint(n,t)}getPoints(e=5){let t=[];for(let n=0;n<=e;n++)t.push(this.getPoint(n/e));return t}getSpacedPoints(e=5){let t=[];for(let n=0;n<=e;n++)t.push(this.getPointAt(n/e));return t}getLength(){let e=this.getLengths();return e[e.length-1]}getLengths(e=this.arcLengthDivisions){if(this.cacheArcLengths&&this.cacheArcLengths.length===e+1&&!this.needsUpdate)return this.cacheArcLengths;this.needsUpdate=!1;let t=[],n,s=this.getPoint(0),r=0;t.push(0);for(let a=1;a<=e;a++)n=this.getPoint(a/e),r+=n.distanceTo(s),t.push(r),s=n;return this.cacheArcLengths=t,t}updateArcLengths(){this.needsUpdate=!0,this.getLengths()}getUtoTmapping(e,t=null){let n=this.getLengths(),s=0,r=n.length,a;t?a=t:a=e*n[r-1];let o=0,c=r-1,l;for(;o<=c;)if(s=Math.floor(o+(c-o)/2),l=n[s]-a,l<0)o=s+1;else if(l>0)c=s-1;else{c=s;break}if(s=c,n[s]===a)return s/(r-1);let h=n[s],d=n[s+1]-h,f=(a-h)/d;return(s+f)/(r-1)}getTangent(e,t){let s=e-1e-4,r=e+1e-4;s<0&&(s=0),r>1&&(r=1);let a=this.getPoint(s),o=this.getPoint(r),c=t||(a.isVector2?new ae:new P);return c.copy(o).sub(a).normalize(),c}getTangentAt(e,t){let n=this.getUtoTmapping(e);return this.getTangent(n,t)}computeFrenetFrames(e,t=!1){let n=new P,s=[],r=[],a=[],o=new P,c=new Ze;for(let f=0;f<=e;f++){let m=f/e;s[f]=this.getTangentAt(m,new P)}r[0]=new P,a[0]=new P;let l=Number.MAX_VALUE,h=Math.abs(s[0].x),u=Math.abs(s[0].y),d=Math.abs(s[0].z);h<=l&&(l=h,n.set(1,0,0)),u<=l&&(l=u,n.set(0,1,0)),d<=l&&n.set(0,0,1),o.crossVectors(s[0],n).normalize(),r[0].crossVectors(s[0],o),a[0].crossVectors(s[0],r[0]);for(let f=1;f<=e;f++){if(r[f]=r[f-1].clone(),a[f]=a[f-1].clone(),o.crossVectors(s[f-1],s[f]),o.length()>Number.EPSILON){o.normalize();let m=Math.acos(it(s[f-1].dot(s[f]),-1,1));r[f].applyMatrix4(c.makeRotationAxis(o,m))}a[f].crossVectors(s[f],r[f])}if(t===!0){let f=Math.acos(it(r[0].dot(r[e]),-1,1));f/=e,s[0].dot(o.crossVectors(r[0],r[e]))>0&&(f=-f);for(let m=1;m<=e;m++)r[m].applyMatrix4(c.makeRotationAxis(s[m],f*m)),a[m].crossVectors(s[m],r[m])}return{tangents:s,normals:r,binormals:a}}clone(){return new this.constructor().copy(this)}copy(e){return this.arcLengthDivisions=e.arcLengthDivisions,this}toJSON(){let e={metadata:{version:4.7,type:"Curve",generator:"Curve.toJSON"}};return e.arcLengthDivisions=this.arcLengthDivisions,e.type=this.type,e}fromJSON(e){return this.arcLengthDivisions=e.arcLengthDivisions,this}},Hr=class extends Gn{constructor(e=0,t=0,n=1,s=1,r=0,a=Math.PI*2,o=!1,c=0){super(),this.isEllipseCurve=!0,this.type="EllipseCurve",this.aX=e,this.aY=t,this.xRadius=n,this.yRadius=s,this.aStartAngle=r,this.aEndAngle=a,this.aClockwise=o,this.aRotation=c}getPoint(e,t=new ae){let n=t,s=Math.PI*2,r=this.aEndAngle-this.aStartAngle,a=Math.abs(r)<Number.EPSILON;for(;r<0;)r+=s;for(;r>s;)r-=s;r<Number.EPSILON&&(a?r=0:r=s),this.aClockwise===!0&&!a&&(r===s?r=-s:r=r-s);let o=this.aStartAngle+e*r,c=this.aX+this.xRadius*Math.cos(o),l=this.aY+this.yRadius*Math.sin(o);if(this.aRotation!==0){let h=Math.cos(this.aRotation),u=Math.sin(this.aRotation),d=c-this.aX,f=l-this.aY;c=d*h-f*u+this.aX,l=d*u+f*h+this.aY}return n.set(c,l)}copy(e){return super.copy(e),this.aX=e.aX,this.aY=e.aY,this.xRadius=e.xRadius,this.yRadius=e.yRadius,this.aStartAngle=e.aStartAngle,this.aEndAngle=e.aEndAngle,this.aClockwise=e.aClockwise,this.aRotation=e.aRotation,this}toJSON(){let e=super.toJSON();return e.aX=this.aX,e.aY=this.aY,e.xRadius=this.xRadius,e.yRadius=this.yRadius,e.aStartAngle=this.aStartAngle,e.aEndAngle=this.aEndAngle,e.aClockwise=this.aClockwise,e.aRotation=this.aRotation,e}fromJSON(e){return super.fromJSON(e),this.aX=e.aX,this.aY=e.aY,this.xRadius=e.xRadius,this.yRadius=e.yRadius,this.aStartAngle=e.aStartAngle,this.aEndAngle=e.aEndAngle,this.aClockwise=e.aClockwise,this.aRotation=e.aRotation,this}},xc=class extends Hr{constructor(e,t,n,s,r,a){super(e,t,n,n,s,r,a),this.isArcCurve=!0,this.type="ArcCurve"}};function pu(){let i=0,e=0,t=0,n=0;function s(r,a,o,c){i=r,e=o,t=-3*r+3*a-2*o-c,n=2*r-2*a+o+c}return{initCatmullRom:function(r,a,o,c,l){s(a,o,l*(o-r),l*(c-a))},initNonuniformCatmullRom:function(r,a,o,c,l,h,u){let d=(a-r)/l-(o-r)/(l+h)+(o-a)/h,f=(o-a)/h-(c-a)/(h+u)+(c-o)/u;d*=h,f*=h,s(a,o,d,f)},calc:function(r){let a=r*r,o=a*r;return i+e*r+t*a+n*o}}}var _f=new P,yf=new P,Eh=new pu,Th=new pu,wh=new pu,Gr=class extends Gn{constructor(e=[],t=!1,n="centripetal",s=.5){super(),this.isCatmullRomCurve3=!0,this.type="CatmullRomCurve3",this.points=e,this.closed=t,this.curveType=n,this.tension=s}getPoint(e,t=new P){let n=t,s=this.points,r=s.length,a=(r-(this.closed?0:1))*e,o=Math.floor(a),c=a-o;this.closed?o+=o>0?0:(Math.floor(Math.abs(o)/r)+1)*r:c===0&&o===r-1&&(o=r-2,c=1);let l,h;this.closed||o>0?l=s[(o-1)%r]:(yf.subVectors(s[0],s[1]).add(s[0]),l=yf);let u=s[o%r],d=s[(o+1)%r];if(this.closed||o+2<r?h=s[(o+2)%r]:(_f.subVectors(s[r-1],s[r-2]).add(s[r-1]),h=_f),this.curveType==="centripetal"||this.curveType==="chordal"){let f=this.curveType==="chordal"?.5:.25,m=Math.pow(l.distanceToSquared(u),f),b=Math.pow(u.distanceToSquared(d),f),g=Math.pow(d.distanceToSquared(h),f);b<1e-4&&(b=1),m<1e-4&&(m=b),g<1e-4&&(g=b),Eh.initNonuniformCatmullRom(l.x,u.x,d.x,h.x,m,b,g),Th.initNonuniformCatmullRom(l.y,u.y,d.y,h.y,m,b,g),wh.initNonuniformCatmullRom(l.z,u.z,d.z,h.z,m,b,g)}else this.curveType==="catmullrom"&&(Eh.initCatmullRom(l.x,u.x,d.x,h.x,this.tension),Th.initCatmullRom(l.y,u.y,d.y,h.y,this.tension),wh.initCatmullRom(l.z,u.z,d.z,h.z,this.tension));return n.set(Eh.calc(c),Th.calc(c),wh.calc(c)),n}copy(e){super.copy(e),this.points=[];for(let t=0,n=e.points.length;t<n;t++){let s=e.points[t];this.points.push(s.clone())}return this.closed=e.closed,this.curveType=e.curveType,this.tension=e.tension,this}toJSON(){let e=super.toJSON();e.points=[];for(let t=0,n=this.points.length;t<n;t++){let s=this.points[t];e.points.push(s.toArray())}return e.closed=this.closed,e.curveType=this.curveType,e.tension=this.tension,e}fromJSON(e){super.fromJSON(e),this.points=[];for(let t=0,n=e.points.length;t<n;t++){let s=e.points[t];this.points.push(new P().fromArray(s))}return this.closed=e.closed,this.curveType=e.curveType,this.tension=e.tension,this}};function vf(i,e,t,n,s){let r=(n-e)*.5,a=(s-t)*.5,o=i*i,c=i*o;return(2*t-2*n+r+a)*c+(-3*t+3*n-2*r-a)*o+r*i+t}function Fm(i,e){let t=1-i;return t*t*e}function Um(i,e){return 2*(1-i)*i*e}function Om(i,e){return i*i*e}function Ma(i,e,t,n){return Fm(i,e)+Um(i,t)+Om(i,n)}function Bm(i,e){let t=1-i;return t*t*t*e}function km(i,e){let t=1-i;return 3*t*t*i*e}function zm(i,e){return 3*(1-i)*i*i*e}function Hm(i,e){return i*i*i*e}function Sa(i,e,t,n,s){return Bm(i,e)+km(i,t)+zm(i,n)+Hm(i,s)}var Oa=class extends Gn{constructor(e=new ae,t=new ae,n=new ae,s=new ae){super(),this.isCubicBezierCurve=!0,this.type="CubicBezierCurve",this.v0=e,this.v1=t,this.v2=n,this.v3=s}getPoint(e,t=new ae){let n=t,s=this.v0,r=this.v1,a=this.v2,o=this.v3;return n.set(Sa(e,s.x,r.x,a.x,o.x),Sa(e,s.y,r.y,a.y,o.y)),n}copy(e){return super.copy(e),this.v0.copy(e.v0),this.v1.copy(e.v1),this.v2.copy(e.v2),this.v3.copy(e.v3),this}toJSON(){let e=super.toJSON();return e.v0=this.v0.toArray(),e.v1=this.v1.toArray(),e.v2=this.v2.toArray(),e.v3=this.v3.toArray(),e}fromJSON(e){return super.fromJSON(e),this.v0.fromArray(e.v0),this.v1.fromArray(e.v1),this.v2.fromArray(e.v2),this.v3.fromArray(e.v3),this}},_c=class extends Gn{constructor(e=new P,t=new P,n=new P,s=new P){super(),this.isCubicBezierCurve3=!0,this.type="CubicBezierCurve3",this.v0=e,this.v1=t,this.v2=n,this.v3=s}getPoint(e,t=new P){let n=t,s=this.v0,r=this.v1,a=this.v2,o=this.v3;return n.set(Sa(e,s.x,r.x,a.x,o.x),Sa(e,s.y,r.y,a.y,o.y),Sa(e,s.z,r.z,a.z,o.z)),n}copy(e){return super.copy(e),this.v0.copy(e.v0),this.v1.copy(e.v1),this.v2.copy(e.v2),this.v3.copy(e.v3),this}toJSON(){let e=super.toJSON();return e.v0=this.v0.toArray(),e.v1=this.v1.toArray(),e.v2=this.v2.toArray(),e.v3=this.v3.toArray(),e}fromJSON(e){return super.fromJSON(e),this.v0.fromArray(e.v0),this.v1.fromArray(e.v1),this.v2.fromArray(e.v2),this.v3.fromArray(e.v3),this}},Ba=class extends Gn{constructor(e=new ae,t=new ae){super(),this.isLineCurve=!0,this.type="LineCurve",this.v1=e,this.v2=t}getPoint(e,t=new ae){let n=t;return e===1?n.copy(this.v2):(n.copy(this.v2).sub(this.v1),n.multiplyScalar(e).add(this.v1)),n}getPointAt(e,t){return this.getPoint(e,t)}getTangent(e,t=new ae){return t.subVectors(this.v2,this.v1).normalize()}getTangentAt(e,t){return this.getTangent(e,t)}copy(e){return super.copy(e),this.v1.copy(e.v1),this.v2.copy(e.v2),this}toJSON(){let e=super.toJSON();return e.v1=this.v1.toArray(),e.v2=this.v2.toArray(),e}fromJSON(e){return super.fromJSON(e),this.v1.fromArray(e.v1),this.v2.fromArray(e.v2),this}},yc=class extends Gn{constructor(e=new P,t=new P){super(),this.isLineCurve3=!0,this.type="LineCurve3",this.v1=e,this.v2=t}getPoint(e,t=new P){let n=t;return e===1?n.copy(this.v2):(n.copy(this.v2).sub(this.v1),n.multiplyScalar(e).add(this.v1)),n}getPointAt(e,t){return this.getPoint(e,t)}getTangent(e,t=new P){return t.subVectors(this.v2,this.v1).normalize()}getTangentAt(e,t){return this.getTangent(e,t)}copy(e){return super.copy(e),this.v1.copy(e.v1),this.v2.copy(e.v2),this}toJSON(){let e=super.toJSON();return e.v1=this.v1.toArray(),e.v2=this.v2.toArray(),e}fromJSON(e){return super.fromJSON(e),this.v1.fromArray(e.v1),this.v2.fromArray(e.v2),this}},ka=class extends Gn{constructor(e=new ae,t=new ae,n=new ae){super(),this.isQuadraticBezierCurve=!0,this.type="QuadraticBezierCurve",this.v0=e,this.v1=t,this.v2=n}getPoint(e,t=new ae){let n=t,s=this.v0,r=this.v1,a=this.v2;return n.set(Ma(e,s.x,r.x,a.x),Ma(e,s.y,r.y,a.y)),n}copy(e){return super.copy(e),this.v0.copy(e.v0),this.v1.copy(e.v1),this.v2.copy(e.v2),this}toJSON(){let e=super.toJSON();return e.v0=this.v0.toArray(),e.v1=this.v1.toArray(),e.v2=this.v2.toArray(),e}fromJSON(e){return super.fromJSON(e),this.v0.fromArray(e.v0),this.v1.fromArray(e.v1),this.v2.fromArray(e.v2),this}},za=class extends Gn{constructor(e=new P,t=new P,n=new P){super(),this.isQuadraticBezierCurve3=!0,this.type="QuadraticBezierCurve3",this.v0=e,this.v1=t,this.v2=n}getPoint(e,t=new P){let n=t,s=this.v0,r=this.v1,a=this.v2;return n.set(Ma(e,s.x,r.x,a.x),Ma(e,s.y,r.y,a.y),Ma(e,s.z,r.z,a.z)),n}copy(e){return super.copy(e),this.v0.copy(e.v0),this.v1.copy(e.v1),this.v2.copy(e.v2),this}toJSON(){let e=super.toJSON();return e.v0=this.v0.toArray(),e.v1=this.v1.toArray(),e.v2=this.v2.toArray(),e}fromJSON(e){return super.fromJSON(e),this.v0.fromArray(e.v0),this.v1.fromArray(e.v1),this.v2.fromArray(e.v2),this}},Ha=class extends Gn{constructor(e=[]){super(),this.isSplineCurve=!0,this.type="SplineCurve",this.points=e}getPoint(e,t=new ae){let n=t,s=this.points,r=(s.length-1)*e,a=Math.floor(r),o=r-a,c=s[a===0?a:a-1],l=s[a],h=s[a>s.length-2?s.length-1:a+1],u=s[a>s.length-3?s.length-1:a+2];return n.set(vf(o,c.x,l.x,h.x,u.x),vf(o,c.y,l.y,h.y,u.y)),n}copy(e){super.copy(e),this.points=[];for(let t=0,n=e.points.length;t<n;t++){let s=e.points[t];this.points.push(s.clone())}return this}toJSON(){let e=super.toJSON();e.points=[];for(let t=0,n=this.points.length;t<n;t++){let s=this.points[t];e.points.push(s.toArray())}return e}fromJSON(e){super.fromJSON(e),this.points=[];for(let t=0,n=e.points.length;t<n;t++){let s=e.points[t];this.points.push(new ae().fromArray(s))}return this}},vc=Object.freeze({__proto__:null,ArcCurve:xc,CatmullRomCurve3:Gr,CubicBezierCurve:Oa,CubicBezierCurve3:_c,EllipseCurve:Hr,LineCurve:Ba,LineCurve3:yc,QuadraticBezierCurve:ka,QuadraticBezierCurve3:za,SplineCurve:Ha}),Mc=class extends Gn{constructor(){super(),this.type="CurvePath",this.curves=[],this.autoClose=!1}add(e){this.curves.push(e)}closePath(){let e=this.curves[0].getPoint(0),t=this.curves[this.curves.length-1].getPoint(1);if(!e.equals(t)){let n=e.isVector2===!0?"LineCurve":"LineCurve3";this.curves.push(new vc[n](t,e))}return this}getPoint(e,t){let n=e*this.getLength(),s=this.getCurveLengths(),r=0;for(;r<s.length;){if(s[r]>=n){let a=s[r]-n,o=this.curves[r],c=o.getLength(),l=c===0?0:1-a/c;return o.getPointAt(l,t)}r++}return null}getLength(){let e=this.getCurveLengths();return e[e.length-1]}updateArcLengths(){this.needsUpdate=!0,this.cacheLengths=null,this.getCurveLengths()}getCurveLengths(){if(this.cacheLengths&&this.cacheLengths.length===this.curves.length)return this.cacheLengths;let e=[],t=0;for(let n=0,s=this.curves.length;n<s;n++)t+=this.curves[n].getLength(),e.push(t);return this.cacheLengths=e,e}getSpacedPoints(e=40){let t=[];for(let n=0;n<=e;n++)t.push(this.getPoint(n/e));return this.autoClose&&t.push(t[0]),t}getPoints(e=12){let t=[],n;for(let s=0,r=this.curves;s<r.length;s++){let a=r[s],o=a.isEllipseCurve?e*2:a.isLineCurve||a.isLineCurve3?1:a.isSplineCurve?e*a.points.length:e,c=a.getPoints(o);for(let l=0;l<c.length;l++){let h=c[l];n&&n.equals(h)||(t.push(h),n=h)}}return this.autoClose&&t.length>1&&!t[t.length-1].equals(t[0])&&t.push(t[0]),t}copy(e){super.copy(e),this.curves=[];for(let t=0,n=e.curves.length;t<n;t++){let s=e.curves[t];this.curves.push(s.clone())}return this.autoClose=e.autoClose,this}toJSON(){let e=super.toJSON();e.autoClose=this.autoClose,e.curves=[];for(let t=0,n=this.curves.length;t<n;t++){let s=this.curves[t];e.curves.push(s.toJSON())}return e}fromJSON(e){super.fromJSON(e),this.autoClose=e.autoClose,this.curves=[];for(let t=0,n=e.curves.length;t<n;t++){let s=e.curves[t];this.curves.push(new vc[s.type]().fromJSON(s))}return this}},Qe=class extends Mc{constructor(e){super(),this.type="Path",this.currentPoint=new ae,e&&this.setFromPoints(e)}setFromPoints(e){this.moveTo(e[0].x,e[0].y);for(let t=1,n=e.length;t<n;t++)this.lineTo(e[t].x,e[t].y);return this}moveTo(e,t){return this.currentPoint.set(e,t),this}lineTo(e,t){let n=new Ba(this.currentPoint.clone(),new ae(e,t));return this.curves.push(n),this.currentPoint.set(e,t),this}quadraticCurveTo(e,t,n,s){let r=new ka(this.currentPoint.clone(),new ae(e,t),new ae(n,s));return this.curves.push(r),this.currentPoint.set(n,s),this}bezierCurveTo(e,t,n,s,r,a){let o=new Oa(this.currentPoint.clone(),new ae(e,t),new ae(n,s),new ae(r,a));return this.curves.push(o),this.currentPoint.set(r,a),this}splineThru(e){let t=[this.currentPoint.clone()].concat(e),n=new Ha(t);return this.curves.push(n),this.currentPoint.copy(e[e.length-1]),this}arc(e,t,n,s,r,a){let o=this.currentPoint.x,c=this.currentPoint.y;return this.absarc(e+o,t+c,n,s,r,a),this}absarc(e,t,n,s,r,a){return this.absellipse(e,t,n,n,s,r,a),this}ellipse(e,t,n,s,r,a,o,c){let l=this.currentPoint.x,h=this.currentPoint.y;return this.absellipse(e+l,t+h,n,s,r,a,o,c),this}absellipse(e,t,n,s,r,a,o,c){let l=new Hr(e,t,n,s,r,a,o,c);if(this.curves.length>0){let u=l.getPoint(0);u.equals(this.currentPoint)||this.lineTo(u.x,u.y)}this.curves.push(l);let h=l.getPoint(1);return this.currentPoint.copy(h),this}copy(e){return super.copy(e),this.currentPoint.copy(e.currentPoint),this}toJSON(){let e=super.toJSON();return e.currentPoint=this.currentPoint.toArray(),e}fromJSON(e){return super.fromJSON(e),this.currentPoint.fromArray(e.currentPoint),this}},Jt=class extends Qe{constructor(e){super(e),this.uuid=ii(),this.type="Shape",this.holes=[]}getPointsHoles(e){let t=[];for(let n=0,s=this.holes.length;n<s;n++)t[n]=this.holes[n].getPoints(e);return t}extractPoints(e){return{shape:this.getPoints(e),holes:this.getPointsHoles(e)}}copy(e){super.copy(e),this.holes=[];for(let t=0,n=e.holes.length;t<n;t++){let s=e.holes[t];this.holes.push(s.clone())}return this}toJSON(){let e=super.toJSON();e.uuid=this.uuid,e.holes=[];for(let t=0,n=this.holes.length;t<n;t++){let s=this.holes[t];e.holes.push(s.toJSON())}return e}fromJSON(e){super.fromJSON(e),this.uuid=e.uuid,this.holes=[];for(let t=0,n=e.holes.length;t<n;t++){let s=e.holes[t];this.holes.push(new Qe().fromJSON(s))}return this}};function Gm(i,e,t=2){let n=e&&e.length,s=n?e[0]*t:i.length,r=_p(i,0,s,t,!0),a=[];if(!r||r.next===r.prev)return a;let o,c,l;if(n&&(r=jm(i,e,r,t)),i.length>80*t){o=i[0],c=i[1];let h=o,u=c;for(let d=t;d<s;d+=t){let f=i[d],m=i[d+1];f<o&&(o=f),m<c&&(c=m),f>h&&(h=f),m>u&&(u=m)}l=Math.max(h-o,u-c),l=l!==0?32767/l:0}return Ga(r,a,t,o,c,l,0),a}function _p(i,e,t,n,s){let r;if(s===sg(i,e,t,n)>0)for(let a=e;a<t;a+=n)r=Mf(a/n|0,i[a],i[a+1],r);else for(let a=t-n;a>=e;a-=n)r=Mf(a/n|0,i[a],i[a+1],r);return r&&Vr(r,r.next)&&(Wa(r),r=r.next),r}function Ys(i,e){if(!i)return i;e||(e=i);let t=i,n;do if(n=!1,!t.steiner&&(Vr(t,t.next)||qt(t.prev,t,t.next)===0)){if(Wa(t),t=e=t.prev,t===t.next)break;n=!0}else t=t.next;while(n||t!==e);return e}function Ga(i,e,t,n,s,r,a){if(!i)return;!a&&r&&$m(i,n,s,r);let o=i;for(;i.prev!==i.next;){let c=i.prev,l=i.next;if(r?Wm(i,n,s,r):Vm(i)){e.push(c.i,i.i,l.i),Wa(i),i=l.next,o=l.next;continue}if(i=l,i===o){a?a===1?(i=qm(Ys(i),e),Ga(i,e,t,n,s,r,2)):a===2&&Xm(i,e,t,n,s,r):Ga(Ys(i),e,t,n,s,r,1);break}}}function Vm(i){let e=i.prev,t=i,n=i.next;if(qt(e,t,n)>=0)return!1;let s=e.x,r=t.x,a=n.x,o=e.y,c=t.y,l=n.y,h=Math.min(s,r,a),u=Math.min(o,c,l),d=Math.max(s,r,a),f=Math.max(o,c,l),m=n.next;for(;m!==e;){if(m.x>=h&&m.x<=d&&m.y>=u&&m.y<=f&&_a(s,o,r,c,a,l,m.x,m.y)&&qt(m.prev,m,m.next)>=0)return!1;m=m.next}return!0}function Wm(i,e,t,n){let s=i.prev,r=i,a=i.next;if(qt(s,r,a)>=0)return!1;let o=s.x,c=r.x,l=a.x,h=s.y,u=r.y,d=a.y,f=Math.min(o,c,l),m=Math.min(h,u,d),b=Math.max(o,c,l),g=Math.max(h,u,d),p=Fh(f,m,e,t,n),_=Fh(b,g,e,t,n),S=i.prevZ,x=i.nextZ;for(;S&&S.z>=p&&x&&x.z<=_;){if(S.x>=f&&S.x<=b&&S.y>=m&&S.y<=g&&S!==s&&S!==a&&_a(o,h,c,u,l,d,S.x,S.y)&&qt(S.prev,S,S.next)>=0||(S=S.prevZ,x.x>=f&&x.x<=b&&x.y>=m&&x.y<=g&&x!==s&&x!==a&&_a(o,h,c,u,l,d,x.x,x.y)&&qt(x.prev,x,x.next)>=0))return!1;x=x.nextZ}for(;S&&S.z>=p;){if(S.x>=f&&S.x<=b&&S.y>=m&&S.y<=g&&S!==s&&S!==a&&_a(o,h,c,u,l,d,S.x,S.y)&&qt(S.prev,S,S.next)>=0)return!1;S=S.prevZ}for(;x&&x.z<=_;){if(x.x>=f&&x.x<=b&&x.y>=m&&x.y<=g&&x!==s&&x!==a&&_a(o,h,c,u,l,d,x.x,x.y)&&qt(x.prev,x,x.next)>=0)return!1;x=x.nextZ}return!0}function qm(i,e){let t=i;do{let n=t.prev,s=t.next.next;!Vr(n,s)&&vp(n,t,t.next,s)&&Va(n,s)&&Va(s,n)&&(e.push(n.i,t.i,s.i),Wa(t),Wa(t.next),t=i=s),t=t.next}while(t!==i);return Ys(t)}function Xm(i,e,t,n,s,r){let a=i;do{let o=a.next.next;for(;o!==a.prev;){if(a.i!==o.i&&tg(a,o)){let c=Mp(a,o);a=Ys(a,a.next),c=Ys(c,c.next),Ga(a,e,t,n,s,r,0),Ga(c,e,t,n,s,r,0);return}o=o.next}a=a.next}while(a!==i)}function jm(i,e,t,n){let s=[];for(let r=0,a=e.length;r<a;r++){let o=e[r]*n,c=r<a-1?e[r+1]*n:i.length,l=_p(i,o,c,n,!1);l===l.next&&(l.steiner=!0),s.push(eg(l))}s.sort(Km);for(let r=0;r<s.length;r++)t=Ym(s[r],t);return t}function Km(i,e){let t=i.x-e.x;if(t===0&&(t=i.y-e.y,t===0)){let n=(i.next.y-i.y)/(i.next.x-i.x),s=(e.next.y-e.y)/(e.next.x-e.x);t=n-s}return t}function Ym(i,e){let t=Jm(i,e);if(!t)return e;let n=Mp(t,i);return Ys(n,n.next),Ys(t,t.next)}function Jm(i,e){let t=e,n=i.x,s=i.y,r=-1/0,a;if(Vr(i,t))return t;do{if(Vr(i,t.next))return t.next;if(s<=t.y&&s>=t.next.y&&t.next.y!==t.y){let u=t.x+(s-t.y)*(t.next.x-t.x)/(t.next.y-t.y);if(u<=n&&u>r&&(r=u,a=t.x<t.next.x?t:t.next,u===n))return a}t=t.next}while(t!==e);if(!a)return null;let o=a,c=a.x,l=a.y,h=1/0;t=a;do{if(n>=t.x&&t.x>=c&&n!==t.x&&yp(s<l?n:r,s,c,l,s<l?r:n,s,t.x,t.y)){let u=Math.abs(s-t.y)/(n-t.x);Va(t,i)&&(u<h||u===h&&(t.x>a.x||t.x===a.x&&Zm(a,t)))&&(a=t,h=u)}t=t.next}while(t!==o);return a}function Zm(i,e){return qt(i.prev,i,e.prev)<0&&qt(e.next,i,i.next)<0}function $m(i,e,t,n){let s=i;do s.z===0&&(s.z=Fh(s.x,s.y,e,t,n)),s.prevZ=s.prev,s.nextZ=s.next,s=s.next;while(s!==i);s.prevZ.nextZ=null,s.prevZ=null,Qm(s)}function Qm(i){let e,t=1;do{let n=i,s;i=null;let r=null;for(e=0;n;){e++;let a=n,o=0;for(let l=0;l<t&&(o++,a=a.nextZ,!!a);l++);let c=t;for(;o>0||c>0&&a;)o!==0&&(c===0||!a||n.z<=a.z)?(s=n,n=n.nextZ,o--):(s=a,a=a.nextZ,c--),r?r.nextZ=s:i=s,s.prevZ=r,r=s;n=a}r.nextZ=null,t*=2}while(e>1);return i}function Fh(i,e,t,n,s){return i=(i-t)*s|0,e=(e-n)*s|0,i=(i|i<<8)&16711935,i=(i|i<<4)&252645135,i=(i|i<<2)&858993459,i=(i|i<<1)&1431655765,e=(e|e<<8)&16711935,e=(e|e<<4)&252645135,e=(e|e<<2)&858993459,e=(e|e<<1)&1431655765,i|e<<1}function eg(i){let e=i,t=i;do(e.x<t.x||e.x===t.x&&e.y<t.y)&&(t=e),e=e.next;while(e!==i);return t}function yp(i,e,t,n,s,r,a,o){return(s-a)*(e-o)>=(i-a)*(r-o)&&(i-a)*(n-o)>=(t-a)*(e-o)&&(t-a)*(r-o)>=(s-a)*(n-o)}function _a(i,e,t,n,s,r,a,o){return!(i===a&&e===o)&&yp(i,e,t,n,s,r,a,o)}function tg(i,e){return i.next.i!==e.i&&i.prev.i!==e.i&&!ng(i,e)&&(Va(i,e)&&Va(e,i)&&ig(i,e)&&(qt(i.prev,i,e.prev)||qt(i,e.prev,e))||Vr(i,e)&&qt(i.prev,i,i.next)>0&&qt(e.prev,e,e.next)>0)}function qt(i,e,t){return(e.y-i.y)*(t.x-e.x)-(e.x-i.x)*(t.y-e.y)}function Vr(i,e){return i.x===e.x&&i.y===e.y}function vp(i,e,t,n){let s=Qo(qt(i,e,t)),r=Qo(qt(i,e,n)),a=Qo(qt(t,n,i)),o=Qo(qt(t,n,e));return!!(s!==r&&a!==o||s===0&&$o(i,t,e)||r===0&&$o(i,n,e)||a===0&&$o(t,i,n)||o===0&&$o(t,e,n))}function $o(i,e,t){return e.x<=Math.max(i.x,t.x)&&e.x>=Math.min(i.x,t.x)&&e.y<=Math.max(i.y,t.y)&&e.y>=Math.min(i.y,t.y)}function Qo(i){return i>0?1:i<0?-1:0}function ng(i,e){let t=i;do{if(t.i!==i.i&&t.next.i!==i.i&&t.i!==e.i&&t.next.i!==e.i&&vp(t,t.next,i,e))return!0;t=t.next}while(t!==i);return!1}function Va(i,e){return qt(i.prev,i,i.next)<0?qt(i,e,i.next)>=0&&qt(i,i.prev,e)>=0:qt(i,e,i.prev)<0||qt(i,i.next,e)<0}function ig(i,e){let t=i,n=!1,s=(i.x+e.x)/2,r=(i.y+e.y)/2;do t.y>r!=t.next.y>r&&t.next.y!==t.y&&s<(t.next.x-t.x)*(r-t.y)/(t.next.y-t.y)+t.x&&(n=!n),t=t.next;while(t!==i);return n}function Mp(i,e){let t=Uh(i.i,i.x,i.y),n=Uh(e.i,e.x,e.y),s=i.next,r=e.prev;return i.next=e,e.prev=i,t.next=s,s.prev=t,n.next=t,t.prev=n,r.next=n,n.prev=r,n}function Mf(i,e,t,n){let s=Uh(i,e,t);return n?(s.next=n.next,s.prev=n,n.next.prev=s,n.next=s):(s.prev=s,s.next=s),s}function Wa(i){i.next.prev=i.prev,i.prev.next=i.next,i.prevZ&&(i.prevZ.nextZ=i.nextZ),i.nextZ&&(i.nextZ.prevZ=i.prevZ)}function Uh(i,e,t){return{i,x:e,y:t,prev:null,next:null,z:0,prevZ:null,nextZ:null,steiner:!1}}function sg(i,e,t,n){let s=0;for(let r=e,a=t-n;r<t;r+=n)s+=(i[a]-i[r])*(i[r+1]+i[a+1]),a=r;return s}var Oh=class{static triangulate(e,t,n=2){return Gm(e,t,n)}},Ri=class i{static area(e){let t=e.length,n=0;for(let s=t-1,r=0;r<t;s=r++)n+=e[s].x*e[r].y-e[r].x*e[s].y;return n*.5}static isClockWise(e){return i.area(e)<0}static triangulateShape(e,t){let n=[],s=[],r=[];Sf(e),Ef(n,e);let a=e.length;t.forEach(Sf);for(let c=0;c<t.length;c++)s.push(a),a+=t[c].length,Ef(n,t[c]);let o=Oh.triangulate(n,s);for(let c=0;c<o.length;c+=3)r.push(o.slice(c,c+3));return r}};function Sf(i){let e=i.length;e>2&&i[e-1].equals(i[0])&&i.pop()}function Ef(i,e){for(let t=0;t<e.length;t++)i.push(e[t].x),i.push(e[t].y)}var ys=class i extends Bt{constructor(e=new Jt([new ae(.5,.5),new ae(-.5,.5),new ae(-.5,-.5),new ae(.5,-.5)]),t={}){super(),this.type="ExtrudeGeometry",this.parameters={shapes:e,options:t},e=Array.isArray(e)?e:[e];let n=this,s=[],r=[];for(let o=0,c=e.length;o<c;o++){let l=e[o];a(l)}this.setAttribute("position",new nt(s,3)),this.setAttribute("uv",new nt(r,2)),this.computeVertexNormals();function a(o){let c=[],l=t.curveSegments!==void 0?t.curveSegments:12,h=t.steps!==void 0?t.steps:1,u=t.depth!==void 0?t.depth:1,d=t.bevelEnabled!==void 0?t.bevelEnabled:!0,f=t.bevelThickness!==void 0?t.bevelThickness:.2,m=t.bevelSize!==void 0?t.bevelSize:f-.1,b=t.bevelOffset!==void 0?t.bevelOffset:0,g=t.bevelSegments!==void 0?t.bevelSegments:3,p=t.extrudePath,_=t.UVGenerator!==void 0?t.UVGenerator:rg,S,x=!1,M,E,A,y;if(p){S=p.getSpacedPoints(h),x=!0,d=!1;let Q=p.isCatmullRomCurve3?p.closed:!1;M=p.computeFrenetFrames(h,Q),E=new P,A=new P,y=new P}d||(g=0,f=0,m=0,b=0);let w=o.extractPoints(l),R=w.shape,I=w.holes;if(!Ri.isClockWise(R)){R=R.reverse();for(let Q=0,ce=I.length;Q<ce;Q++){let le=I[Q];Ri.isClockWise(le)&&(I[Q]=le.reverse())}}function F(Q){let le=10000000000000001e-36,de=Q[0];for(let xe=1;xe<=Q.length;xe++){let Xe=xe%Q.length,Fe=Q[Xe],qe=Fe.x-de.x,Je=Fe.y-de.y,N=qe*qe+Je*Je,xt=Math.max(Math.abs(Fe.x),Math.abs(Fe.y),Math.abs(de.x),Math.abs(de.y)),st=le*xt*xt;if(N<=st){Q.splice(Xe,1),xe--;continue}de=Fe}}F(R),I.forEach(F);let D=I.length,k=R;for(let Q=0;Q<D;Q++){let ce=I[Q];R=R.concat(ce)}function W(Q,ce,le){return ce||Ye("ExtrudeGeometry: vec does not exist"),Q.clone().addScaledVector(ce,le)}let X=R.length;function re(Q,ce,le){let de,xe,Xe,Fe=Q.x-ce.x,qe=Q.y-ce.y,Je=le.x-Q.x,N=le.y-Q.y,xt=Fe*Fe+qe*qe,st=Fe*N-qe*Je;if(Math.abs(st)>Number.EPSILON){let C=Math.sqrt(xt),v=Math.sqrt(Je*Je+N*N),z=ce.x-qe/C,V=ce.y+Fe/C,Y=le.x-N/v,pe=le.y+Je/v,me=((Y-z)*N-(pe-V)*Je)/(Fe*N-qe*Je);de=z+Fe*me-Q.x,xe=V+qe*me-Q.y;let Z=de*de+xe*xe;if(Z<=2)return new ae(de,xe);Xe=Math.sqrt(Z/2)}else{let C=!1;Fe>Number.EPSILON?Je>Number.EPSILON&&(C=!0):Fe<-Number.EPSILON?Je<-Number.EPSILON&&(C=!0):Math.sign(qe)===Math.sign(N)&&(C=!0),C?(de=-qe,xe=Fe,Xe=Math.sqrt(xt)):(de=Fe,xe=qe,Xe=Math.sqrt(xt/2))}return new ae(de/Xe,xe/Xe)}let j=[];for(let Q=0,ce=k.length,le=ce-1,de=Q+1;Q<ce;Q++,le++,de++)le===ce&&(le=0),de===ce&&(de=0),j[Q]=re(k[Q],k[le],k[de]);let ee=[],te,Ne=j.concat();for(let Q=0,ce=D;Q<ce;Q++){let le=I[Q];te=[];for(let de=0,xe=le.length,Xe=xe-1,Fe=de+1;de<xe;de++,Xe++,Fe++)Xe===xe&&(Xe=0),Fe===xe&&(Fe=0),te[de]=re(le[de],le[Xe],le[Fe]);ee.push(te),Ne=Ne.concat(te)}let Se;if(g===0)Se=Ri.triangulateShape(k,I);else{let Q=[],ce=[];for(let le=0;le<g;le++){let de=le/g,xe=f*Math.cos(de*Math.PI/2),Xe=m*Math.sin(de*Math.PI/2)+b;for(let Fe=0,qe=k.length;Fe<qe;Fe++){let Je=W(k[Fe],j[Fe],Xe);ve(Je.x,Je.y,-xe),de===0&&Q.push(Je)}for(let Fe=0,qe=D;Fe<qe;Fe++){let Je=I[Fe];te=ee[Fe];let N=[];for(let xt=0,st=Je.length;xt<st;xt++){let C=W(Je[xt],te[xt],Xe);ve(C.x,C.y,-xe),de===0&&N.push(C)}de===0&&ce.push(N)}}Se=Ri.triangulateShape(Q,ce)}let ct=Se.length,$e=m+b;for(let Q=0;Q<X;Q++){let ce=d?W(R[Q],Ne[Q],$e):R[Q];x?(A.copy(M.normals[0]).multiplyScalar(ce.x),E.copy(M.binormals[0]).multiplyScalar(ce.y),y.copy(S[0]).add(A).add(E),ve(y.x,y.y,y.z)):ve(ce.x,ce.y,0)}for(let Q=1;Q<=h;Q++)for(let ce=0;ce<X;ce++){let le=d?W(R[ce],Ne[ce],$e):R[ce];x?(A.copy(M.normals[Q]).multiplyScalar(le.x),E.copy(M.binormals[Q]).multiplyScalar(le.y),y.copy(S[Q]).add(A).add(E),ve(y.x,y.y,y.z)):ve(le.x,le.y,u/h*Q)}for(let Q=g-1;Q>=0;Q--){let ce=Q/g,le=f*Math.cos(ce*Math.PI/2),de=m*Math.sin(ce*Math.PI/2)+b;for(let xe=0,Xe=k.length;xe<Xe;xe++){let Fe=W(k[xe],j[xe],de);ve(Fe.x,Fe.y,u+le)}for(let xe=0,Xe=I.length;xe<Xe;xe++){let Fe=I[xe];te=ee[xe];for(let qe=0,Je=Fe.length;qe<Je;qe++){let N=W(Fe[qe],te[qe],de);x?ve(N.x,N.y+S[h-1].y,S[h-1].x+le):ve(N.x,N.y,u+le)}}}ht(),J();function ht(){let Q=s.length/3;if(d){let ce=0,le=X*ce;for(let de=0;de<ct;de++){let xe=Se[de];Ke(xe[2]+le,xe[1]+le,xe[0]+le)}ce=h+g*2,le=X*ce;for(let de=0;de<ct;de++){let xe=Se[de];Ke(xe[0]+le,xe[1]+le,xe[2]+le)}}else{for(let ce=0;ce<ct;ce++){let le=Se[ce];Ke(le[2],le[1],le[0])}for(let ce=0;ce<ct;ce++){let le=Se[ce];Ke(le[0]+X*h,le[1]+X*h,le[2]+X*h)}}n.addGroup(Q,s.length/3-Q,0)}function J(){let Q=s.length/3,ce=0;ie(k,ce),ce+=k.length;for(let le=0,de=I.length;le<de;le++){let xe=I[le];ie(xe,ce),ce+=xe.length}n.addGroup(Q,s.length/3-Q,1)}function ie(Q,ce){let le=Q.length;for(;--le>=0;){let de=le,xe=le-1;xe<0&&(xe=Q.length-1);for(let Xe=0,Fe=h+g*2;Xe<Fe;Xe++){let qe=X*Xe,Je=X*(Xe+1),N=ce+de+qe,xt=ce+xe+qe,st=ce+xe+Je,C=ce+de+Je;Ae(N,xt,st,C)}}}function ve(Q,ce,le){c.push(Q),c.push(ce),c.push(le)}function Ke(Q,ce,le){ze(Q),ze(ce),ze(le);let de=s.length/3,xe=_.generateTopUV(n,s,de-3,de-2,de-1);bt(xe[0]),bt(xe[1]),bt(xe[2])}function Ae(Q,ce,le,de){ze(Q),ze(ce),ze(de),ze(ce),ze(le),ze(de);let xe=s.length/3,Xe=_.generateSideWallUV(n,s,xe-6,xe-3,xe-2,xe-1);bt(Xe[0]),bt(Xe[1]),bt(Xe[3]),bt(Xe[1]),bt(Xe[2]),bt(Xe[3])}function ze(Q){s.push(c[Q*3+0]),s.push(c[Q*3+1]),s.push(c[Q*3+2])}function bt(Q){r.push(Q.x),r.push(Q.y)}}}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}toJSON(){let e=super.toJSON(),t=this.parameters.shapes,n=this.parameters.options;return ag(t,n,e)}static fromJSON(e,t){let n=[];for(let r=0,a=e.shapes.length;r<a;r++){let o=t[e.shapes[r]];n.push(o)}let s=e.options.extrudePath;return s!==void 0&&(e.options.extrudePath=new vc[s.type]().fromJSON(s)),new i(n,e.options)}},rg={generateTopUV:function(i,e,t,n,s){let r=e[t*3],a=e[t*3+1],o=e[n*3],c=e[n*3+1],l=e[s*3],h=e[s*3+1];return[new ae(r,a),new ae(o,c),new ae(l,h)]},generateSideWallUV:function(i,e,t,n,s,r){let a=e[t*3],o=e[t*3+1],c=e[t*3+2],l=e[n*3],h=e[n*3+1],u=e[n*3+2],d=e[s*3],f=e[s*3+1],m=e[s*3+2],b=e[r*3],g=e[r*3+1],p=e[r*3+2];return Math.abs(o-h)<Math.abs(a-l)?[new ae(a,1-c),new ae(l,1-u),new ae(d,1-m),new ae(b,1-p)]:[new ae(o,1-c),new ae(h,1-u),new ae(f,1-m),new ae(g,1-p)]}};function ag(i,e,t){if(t.shapes=[],Array.isArray(i))for(let n=0,s=i.length;n<s;n++){let r=i[n];t.shapes.push(r.uuid)}else t.shapes.push(i.uuid);return t.options=Object.assign({},e),e.extrudePath!==void 0&&(t.options.extrudePath=e.extrudePath.toJSON()),t}var qa=class i extends Bt{constructor(e=[new ae(0,-.5),new ae(.5,0),new ae(0,.5)],t=12,n=0,s=Math.PI*2){super(),this.type="LatheGeometry",this.parameters={points:e,segments:t,phiStart:n,phiLength:s},t=Math.floor(t),s=it(s,0,Math.PI*2);let r=[],a=[],o=[],c=[],l=[],h=1/t,u=new P,d=new ae,f=new P,m=new P,b=new P,g=0,p=0;for(let _=0;_<=e.length-1;_++)switch(_){case 0:g=e[_+1].x-e[_].x,p=e[_+1].y-e[_].y,f.x=p*1,f.y=-g,f.z=p*0,b.copy(f),f.normalize(),c.push(f.x,f.y,f.z);break;case e.length-1:c.push(b.x,b.y,b.z);break;default:g=e[_+1].x-e[_].x,p=e[_+1].y-e[_].y,f.x=p*1,f.y=-g,f.z=p*0,m.copy(f),f.x+=b.x,f.y+=b.y,f.z+=b.z,f.normalize(),c.push(f.x,f.y,f.z),b.copy(m)}for(let _=0;_<=t;_++){let S=n+_*h*s,x=Math.sin(S),M=Math.cos(S);for(let E=0;E<=e.length-1;E++){u.x=e[E].x*x,u.y=e[E].y,u.z=e[E].x*M,a.push(u.x,u.y,u.z),d.x=_/t,d.y=E/(e.length-1),o.push(d.x,d.y);let A=c[3*E+0]*x,y=c[3*E+1],w=c[3*E+0]*M;l.push(A,y,w)}}for(let _=0;_<t;_++)for(let S=0;S<e.length-1;S++){let x=S+_*e.length,M=x,E=x+e.length,A=x+e.length+1,y=x+1;r.push(M,E,y),r.push(A,y,E)}this.setIndex(r),this.setAttribute("position",new nt(a,3)),this.setAttribute("uv",new nt(o,2)),this.setAttribute("normal",new nt(l,3))}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}static fromJSON(e){return new i(e.points,e.segments,e.phiStart,e.phiLength)}};var Vn=class i extends Bt{constructor(e=1,t=1,n=1,s=1){super(),this.type="PlaneGeometry",this.parameters={width:e,height:t,widthSegments:n,heightSegments:s};let r=e/2,a=t/2,o=Math.floor(n),c=Math.floor(s),l=o+1,h=c+1,u=e/o,d=t/c,f=[],m=[],b=[],g=[];for(let p=0;p<h;p++){let _=p*d-a;for(let S=0;S<l;S++){let x=S*u-r;m.push(x,-_,0),b.push(0,0,1),g.push(S/o),g.push(1-p/c)}}for(let p=0;p<c;p++)for(let _=0;_<o;_++){let S=_+l*p,x=_+l*(p+1),M=_+1+l*(p+1),E=_+1+l*p;f.push(S,x,E),f.push(x,M,E)}this.setIndex(f),this.setAttribute("position",new nt(m,3)),this.setAttribute("normal",new nt(b,3)),this.setAttribute("uv",new nt(g,2))}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}static fromJSON(e){return new i(e.width,e.height,e.widthSegments,e.heightSegments)}};var Di=class i extends Bt{constructor(e=new Jt([new ae(0,.5),new ae(-.5,-.5),new ae(.5,-.5)]),t=12){super(),this.type="ShapeGeometry",this.parameters={shapes:e,curveSegments:t};let n=[],s=[],r=[],a=[],o=0,c=0;if(Array.isArray(e)===!1)l(e);else for(let h=0;h<e.length;h++)l(e[h]),this.addGroup(o,c,h),o+=c,c=0;this.setIndex(n),this.setAttribute("position",new nt(s,3)),this.setAttribute("normal",new nt(r,3)),this.setAttribute("uv",new nt(a,2));function l(h){let u=s.length/3,d=h.extractPoints(t),f=d.shape,m=d.holes;Ri.isClockWise(f)===!1&&(f=f.reverse());for(let g=0,p=m.length;g<p;g++){let _=m[g];Ri.isClockWise(_)===!0&&(m[g]=_.reverse())}let b=Ri.triangulateShape(f,m);for(let g=0,p=m.length;g<p;g++){let _=m[g];f=f.concat(_)}for(let g=0,p=f.length;g<p;g++){let _=f[g];s.push(_.x,_.y,0),r.push(0,0,1),a.push(_.x,_.y)}for(let g=0,p=b.length;g<p;g++){let _=b[g],S=_[0]+u,x=_[1]+u,M=_[2]+u;n.push(S,x,M),c+=3}}}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}toJSON(){let e=super.toJSON(),t=this.parameters.shapes;return og(t,e)}static fromJSON(e,t){let n=[];for(let s=0,r=e.shapes.length;s<r;s++){let a=t[e.shapes[s]];n.push(a)}return new i(n,e.curveSegments)}};function og(i,e){if(e.shapes=[],Array.isArray(i))for(let t=0,n=i.length;t<n;t++){let s=i[t];e.shapes.push(s.uuid)}else e.shapes.push(i.uuid);return e}var vs=class i extends Bt{constructor(e=1,t=32,n=16,s=0,r=Math.PI*2,a=0,o=Math.PI){super(),this.type="SphereGeometry",this.parameters={radius:e,widthSegments:t,heightSegments:n,phiStart:s,phiLength:r,thetaStart:a,thetaLength:o},t=Math.max(3,Math.floor(t)),n=Math.max(2,Math.floor(n));let c=Math.min(a+o,Math.PI),l=0,h=[],u=new P,d=new P,f=[],m=[],b=[],g=[];for(let p=0;p<=n;p++){let _=[],S=p/n,x=a+S*o,M=e*Math.cos(x),E=Math.sqrt(e*e-M*M),A=0;p===0&&a===0?A=.5/t:p===n&&c===Math.PI&&(A=-.5/t);for(let y=0;y<=t;y++){let w=y/t,R=s+w*r;u.x=-E*Math.cos(R),u.y=M,u.z=E*Math.sin(R),m.push(u.x,u.y,u.z),d.copy(u).normalize(),b.push(d.x,d.y,d.z),g.push(w+A,1-S),_.push(l++)}h.push(_)}for(let p=0;p<n;p++)for(let _=0;_<t;_++){let S=h[p][_+1],x=h[p][_],M=h[p+1][_],E=h[p+1][_+1];(p!==0||a>0)&&f.push(S,x,E),(p!==n-1||c<Math.PI)&&f.push(x,M,E)}this.setIndex(f),this.setAttribute("position",new nt(m,3)),this.setAttribute("normal",new nt(b,3)),this.setAttribute("uv",new nt(g,2))}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}static fromJSON(e){return new i(e.radius,e.widthSegments,e.heightSegments,e.phiStart,e.phiLength,e.thetaStart,e.thetaLength)}};var Ms=class i extends Bt{constructor(e=1,t=.4,n=12,s=48,r=Math.PI*2,a=0,o=Math.PI*2){super(),this.type="TorusGeometry",this.parameters={radius:e,tube:t,radialSegments:n,tubularSegments:s,arc:r,thetaStart:a,thetaLength:o},n=Math.floor(n),s=Math.floor(s);let c=[],l=[],h=[],u=[],d=new P,f=new P,m=new P;for(let b=0;b<=n;b++){let g=a+b/n*o;for(let p=0;p<=s;p++){let _=p/s*r;f.x=(e+t*Math.cos(g))*Math.cos(_),f.y=(e+t*Math.cos(g))*Math.sin(_),f.z=t*Math.sin(g),l.push(f.x,f.y,f.z),d.x=e*Math.cos(_),d.y=e*Math.sin(_),m.subVectors(f,d).normalize(),h.push(m.x,m.y,m.z),u.push(p/s),u.push(b/n)}}for(let b=1;b<=n;b++)for(let g=1;g<=s;g++){let p=(s+1)*b+g-1,_=(s+1)*(b-1)+g-1,S=(s+1)*(b-1)+g,x=(s+1)*b+g;c.push(p,_,x),c.push(_,S,x)}this.setIndex(c),this.setAttribute("position",new nt(l,3)),this.setAttribute("normal",new nt(h,3)),this.setAttribute("uv",new nt(u,2))}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}static fromJSON(e){return new i(e.radius,e.tube,e.radialSegments,e.tubularSegments,e.arc,e.thetaStart,e.thetaLength)}};var Xa=class i extends Bt{constructor(e=new za(new P(-1,-1,0),new P(-1,1,0),new P(1,1,0)),t=64,n=1,s=8,r=!1){super(),this.type="TubeGeometry",this.parameters={path:e,tubularSegments:t,radius:n,radialSegments:s,closed:r};let a=e.computeFrenetFrames(t,r);this.tangents=a.tangents,this.normals=a.normals,this.binormals=a.binormals;let o=new P,c=new P,l=new ae,h=new P,u=[],d=[],f=[],m=[];b(),this.setIndex(m),this.setAttribute("position",new nt(u,3)),this.setAttribute("normal",new nt(d,3)),this.setAttribute("uv",new nt(f,2));function b(){for(let S=0;S<t;S++)g(S);g(r===!1?t:0),_(),p()}function g(S){h=e.getPointAt(S/t,h);let x=a.normals[S],M=a.binormals[S];for(let E=0;E<=s;E++){let A=E/s*Math.PI*2,y=Math.sin(A),w=-Math.cos(A);c.x=w*x.x+y*M.x,c.y=w*x.y+y*M.y,c.z=w*x.z+y*M.z,c.normalize(),d.push(c.x,c.y,c.z),o.x=h.x+n*c.x,o.y=h.y+n*c.y,o.z=h.z+n*c.z,u.push(o.x,o.y,o.z)}}function p(){for(let S=1;S<=t;S++)for(let x=1;x<=s;x++){let M=(s+1)*(S-1)+(x-1),E=(s+1)*S+(x-1),A=(s+1)*S+x,y=(s+1)*(S-1)+x;m.push(M,E,y),m.push(E,A,y)}}function _(){for(let S=0;S<=t;S++)for(let x=0;x<=s;x++)l.x=S/t,l.y=x/s,f.push(l.x,l.y)}}copy(e){return super.copy(e),this.parameters=Object.assign({},e.parameters),this}toJSON(){let e=super.toJSON();return e.path=this.parameters.path.toJSON(),e}static fromJSON(e){return new i(new vc[e.path.type]().fromJSON(e.path),e.tubularSegments,e.radius,e.radialSegments,e.closed)}};function ir(i){let e={};for(let t in i){e[t]={};for(let n in i[t]){let s=i[t][n];if(Tf(s))s.isRenderTargetTexture?(Ve("UniformsUtils: Textures of render targets cannot be cloned via cloneUniforms() or mergeUniforms()."),e[t][n]=null):e[t][n]=s.clone();else if(Array.isArray(s))if(Tf(s[0])){let r=[];for(let a=0,o=s.length;a<o;a++)r[a]=s[a].clone();e[t][n]=r}else e[t][n]=s.slice();else e[t][n]=s}}return e}function An(i){let e={};for(let t=0;t<i.length;t++){let n=ir(i[t]);for(let s in n)e[s]=n[s]}return e}function Tf(i){return i&&(i.isColor||i.isMatrix3||i.isMatrix4||i.isVector2||i.isVector3||i.isVector4||i.isTexture||i.isQuaternion)}function cg(i){let e=[];for(let t=0;t<i.length;t++)e.push(i[t].clone());return e}function mu(i){let e=i.getRenderTarget();return e===null?i.outputColorSpace:e.isXRRenderTarget===!0?e.texture.colorSpace:lt.workingColorSpace}var Sp={clone:ir,merge:An},lg=`void main() {
	gl_Position = projectionMatrix * modelViewMatrix * vec4( position, 1.0 );
}`,hg=`void main() {
	gl_FragColor = vec4( 1.0, 0.0, 0.0, 1.0 );
}`,Wn=class extends Nn{constructor(e){super(),this.isShaderMaterial=!0,this.type="ShaderMaterial",this.defines={},this.uniforms={},this.uniformsGroups=[],this.vertexShader=lg,this.fragmentShader=hg,this.linewidth=1,this.wireframe=!1,this.wireframeLinewidth=1,this.fog=!1,this.lights=!1,this.clipping=!1,this.forceSinglePass=!0,this.extensions={clipCullDistance:!1,multiDraw:!1},this.defaultAttributeValues={color:[1,1,1],uv:[0,0],uv1:[0,0]},this.index0AttributeName=void 0,this.uniformsNeedUpdate=!1,this.glslVersion=null,e!==void 0&&this.setValues(e)}copy(e){return super.copy(e),this.fragmentShader=e.fragmentShader,this.vertexShader=e.vertexShader,this.uniforms=ir(e.uniforms),this.uniformsGroups=cg(e.uniformsGroups),this.defines=Object.assign({},e.defines),this.wireframe=e.wireframe,this.wireframeLinewidth=e.wireframeLinewidth,this.fog=e.fog,this.lights=e.lights,this.clipping=e.clipping,this.extensions=Object.assign({},e.extensions),this.glslVersion=e.glslVersion,this.defaultAttributeValues=Object.assign({},e.defaultAttributeValues),this.index0AttributeName=e.index0AttributeName,this.uniformsNeedUpdate=e.uniformsNeedUpdate,this}toJSON(e){let t=super.toJSON(e);t.glslVersion=this.glslVersion,t.uniforms={};for(let s in this.uniforms){let a=this.uniforms[s].value;a&&a.isTexture?t.uniforms[s]={type:"t",value:a.toJSON(e).uuid}:a&&a.isColor?t.uniforms[s]={type:"c",value:a.getHex()}:a&&a.isVector2?t.uniforms[s]={type:"v2",value:a.toArray()}:a&&a.isVector3?t.uniforms[s]={type:"v3",value:a.toArray()}:a&&a.isVector4?t.uniforms[s]={type:"v4",value:a.toArray()}:a&&a.isMatrix3?t.uniforms[s]={type:"m3",value:a.toArray()}:a&&a.isMatrix4?t.uniforms[s]={type:"m4",value:a.toArray()}:t.uniforms[s]={value:a}}Object.keys(this.defines).length>0&&(t.defines=this.defines),t.vertexShader=this.vertexShader,t.fragmentShader=this.fragmentShader,t.lights=this.lights,t.clipping=this.clipping;let n={};for(let s in this.extensions)this.extensions[s]===!0&&(n[s]=!0);return Object.keys(n).length>0&&(t.extensions=n),t}fromJSON(e,t){if(super.fromJSON(e,t),e.uniforms!==void 0)for(let n in e.uniforms){let s=e.uniforms[n];switch(this.uniforms[n]={},s.type){case"t":this.uniforms[n].value=t[s.value]||null;break;case"c":this.uniforms[n].value=new Be().setHex(s.value);break;case"v2":this.uniforms[n].value=new ae().fromArray(s.value);break;case"v3":this.uniforms[n].value=new P().fromArray(s.value);break;case"v4":this.uniforms[n].value=new Pt().fromArray(s.value);break;case"m3":this.uniforms[n].value=new et().fromArray(s.value);break;case"m4":this.uniforms[n].value=new Ze().fromArray(s.value);break;default:this.uniforms[n].value=s.value}}if(e.defines!==void 0&&(this.defines=e.defines),e.vertexShader!==void 0&&(this.vertexShader=e.vertexShader),e.fragmentShader!==void 0&&(this.fragmentShader=e.fragmentShader),e.glslVersion!==void 0&&(this.glslVersion=e.glslVersion),e.extensions!==void 0)for(let n in e.extensions)this.extensions[n]=e.extensions[n];return e.lights!==void 0&&(this.lights=e.lights),e.clipping!==void 0&&(this.clipping=e.clipping),this}},Sc=class extends Wn{constructor(e){super(e),this.isRawShaderMaterial=!0,this.type="RawShaderMaterial"}},mt=class extends Nn{constructor(e){super(),this.isMeshStandardMaterial=!0,this.type="MeshStandardMaterial",this.defines={STANDARD:""},this.color=new Be(16777215),this.roughness=1,this.metalness=0,this.map=null,this.lightMap=null,this.lightMapIntensity=1,this.aoMap=null,this.aoMapIntensity=1,this.emissive=new Be(0),this.emissiveIntensity=1,this.emissiveMap=null,this.bumpMap=null,this.bumpScale=1,this.normalMap=null,this.normalMapType=lo,this.normalScale=new ae(1,1),this.displacementMap=null,this.displacementScale=1,this.displacementBias=0,this.roughnessMap=null,this.metalnessMap=null,this.alphaMap=null,this.envMap=null,this.envMapRotation=new si,this.envMapIntensity=1,this.wireframe=!1,this.wireframeLinewidth=1,this.wireframeLinecap="round",this.wireframeLinejoin="round",this.flatShading=!1,this.fog=!0,this.setValues(e)}copy(e){return super.copy(e),this.defines={STANDARD:""},this.color.copy(e.color),this.roughness=e.roughness,this.metalness=e.metalness,this.map=e.map,this.lightMap=e.lightMap,this.lightMapIntensity=e.lightMapIntensity,this.aoMap=e.aoMap,this.aoMapIntensity=e.aoMapIntensity,this.emissive.copy(e.emissive),this.emissiveMap=e.emissiveMap,this.emissiveIntensity=e.emissiveIntensity,this.bumpMap=e.bumpMap,this.bumpScale=e.bumpScale,this.normalMap=e.normalMap,this.normalMapType=e.normalMapType,this.normalScale.copy(e.normalScale),this.displacementMap=e.displacementMap,this.displacementScale=e.displacementScale,this.displacementBias=e.displacementBias,this.roughnessMap=e.roughnessMap,this.metalnessMap=e.metalnessMap,this.alphaMap=e.alphaMap,this.envMap=e.envMap,this.envMapRotation.copy(e.envMapRotation),this.envMapIntensity=e.envMapIntensity,this.wireframe=e.wireframe,this.wireframeLinewidth=e.wireframeLinewidth,this.wireframeLinecap=e.wireframeLinecap,this.wireframeLinejoin=e.wireframeLinejoin,this.flatShading=e.flatShading,this.fog=e.fog,this}},_n=class extends mt{constructor(e){super(),this.isMeshPhysicalMaterial=!0,this.defines={STANDARD:"",PHYSICAL:""},this.type="MeshPhysicalMaterial",this.anisotropyRotation=0,this.anisotropyMap=null,this.clearcoatMap=null,this.clearcoatRoughness=0,this.clearcoatRoughnessMap=null,this.clearcoatNormalScale=new ae(1,1),this.clearcoatNormalMap=null,this.ior=1.5,Object.defineProperty(this,"reflectivity",{get:function(){return it(2.5*(this.ior-1)/(this.ior+1),0,1)},set:function(t){this.ior=(1+.4*t)/(1-.4*t)}}),this.iridescenceMap=null,this.iridescenceIOR=1.3,this.iridescenceThicknessRange=[100,400],this.iridescenceThicknessMap=null,this.sheenColor=new Be(0),this.sheenColorMap=null,this.sheenRoughness=1,this.sheenRoughnessMap=null,this.transmissionMap=null,this.thickness=0,this.thicknessMap=null,this.attenuationDistance=1/0,this.attenuationColor=new Be(1,1,1),this.specularIntensity=1,this.specularIntensityMap=null,this.specularColor=new Be(1,1,1),this.specularColorMap=null,this._anisotropy=0,this._clearcoat=0,this._dispersion=0,this._iridescence=0,this._retroreflectivity=0,this._sheen=0,this._transmission=0,this.setValues(e)}get anisotropy(){return this._anisotropy}set anisotropy(e){this._anisotropy>0!=e>0&&this.version++,this._anisotropy=e}get clearcoat(){return this._clearcoat}set clearcoat(e){this._clearcoat>0!=e>0&&this.version++,this._clearcoat=e}get iridescence(){return this._iridescence}set iridescence(e){this._iridescence>0!=e>0&&this.version++,this._iridescence=e}get dispersion(){return this._dispersion}set dispersion(e){this._dispersion>0!=e>0&&this.version++,this._dispersion=e}get retroreflectivity(){return this._retroreflectivity}set retroreflectivity(e){this._retroreflectivity>0!=e>0&&this.version++,this._retroreflectivity=e}get sheen(){return this._sheen}set sheen(e){this._sheen>0!=e>0&&this.version++,this._sheen=e}get transmission(){return this._transmission}set transmission(e){this._transmission>0!=e>0&&this.version++,this._transmission=e}copy(e){return super.copy(e),this.defines={STANDARD:"",PHYSICAL:""},this.anisotropy=e.anisotropy,this.anisotropyRotation=e.anisotropyRotation,this.anisotropyMap=e.anisotropyMap,this.clearcoat=e.clearcoat,this.clearcoatMap=e.clearcoatMap,this.clearcoatRoughness=e.clearcoatRoughness,this.clearcoatRoughnessMap=e.clearcoatRoughnessMap,this.clearcoatNormalMap=e.clearcoatNormalMap,this.clearcoatNormalScale.copy(e.clearcoatNormalScale),this.dispersion=e.dispersion,this.ior=e.ior,this.iridescence=e.iridescence,this.iridescenceMap=e.iridescenceMap,this.iridescenceIOR=e.iridescenceIOR,this.iridescenceThicknessRange=[...e.iridescenceThicknessRange],this.iridescenceThicknessMap=e.iridescenceThicknessMap,this.retroreflectivity=e.retroreflectivity,this.sheen=e.sheen,this.sheenColor.copy(e.sheenColor),this.sheenColorMap=e.sheenColorMap,this.sheenRoughness=e.sheenRoughness,this.sheenRoughnessMap=e.sheenRoughnessMap,this.transmission=e.transmission,this.transmissionMap=e.transmissionMap,this.thickness=e.thickness,this.thicknessMap=e.thicknessMap,this.attenuationDistance=e.attenuationDistance,this.attenuationColor.copy(e.attenuationColor),this.specularIntensity=e.specularIntensity,this.specularIntensityMap=e.specularIntensityMap,this.specularColor.copy(e.specularColor),this.specularColorMap=e.specularColorMap,this}};var ai=class extends Nn{constructor(e){super(),this.isMeshLambertMaterial=!0,this.type="MeshLambertMaterial",this.color=new Be(16777215),this.map=null,this.lightMap=null,this.lightMapIntensity=1,this.aoMap=null,this.aoMapIntensity=1,this.emissive=new Be(0),this.emissiveIntensity=1,this.emissiveMap=null,this.bumpMap=null,this.bumpScale=1,this.normalMap=null,this.normalMapType=lo,this.normalScale=new ae(1,1),this.displacementMap=null,this.displacementScale=1,this.displacementBias=0,this.specularMap=null,this.alphaMap=null,this.envMap=null,this.envMapRotation=new si,this.combine=Uc,this.reflectivity=1,this.envMapIntensity=1,this.refractionRatio=.98,this.wireframe=!1,this.wireframeLinewidth=1,this.wireframeLinecap="round",this.wireframeLinejoin="round",this.flatShading=!1,this.fog=!0,this.setValues(e)}copy(e){return super.copy(e),this.color.copy(e.color),this.map=e.map,this.lightMap=e.lightMap,this.lightMapIntensity=e.lightMapIntensity,this.aoMap=e.aoMap,this.aoMapIntensity=e.aoMapIntensity,this.emissive.copy(e.emissive),this.emissiveMap=e.emissiveMap,this.emissiveIntensity=e.emissiveIntensity,this.bumpMap=e.bumpMap,this.bumpScale=e.bumpScale,this.normalMap=e.normalMap,this.normalMapType=e.normalMapType,this.normalScale.copy(e.normalScale),this.displacementMap=e.displacementMap,this.displacementScale=e.displacementScale,this.displacementBias=e.displacementBias,this.specularMap=e.specularMap,this.alphaMap=e.alphaMap,this.envMap=e.envMap,this.envMapRotation.copy(e.envMapRotation),this.combine=e.combine,this.reflectivity=e.reflectivity,this.envMapIntensity=e.envMapIntensity,this.refractionRatio=e.refractionRatio,this.wireframe=e.wireframe,this.wireframeLinewidth=e.wireframeLinewidth,this.wireframeLinecap=e.wireframeLinecap,this.wireframeLinejoin=e.wireframeLinejoin,this.flatShading=e.flatShading,this.fog=e.fog,this}},Ec=class extends Nn{constructor(e){super(),this.isMeshDepthMaterial=!0,this.type="MeshDepthMaterial",this.depthPacking=rp,this.map=null,this.alphaMap=null,this.displacementMap=null,this.displacementScale=1,this.displacementBias=0,this.wireframe=!1,this.wireframeLinewidth=1,this.setValues(e)}copy(e){return super.copy(e),this.depthPacking=e.depthPacking,this.map=e.map,this.alphaMap=e.alphaMap,this.displacementMap=e.displacementMap,this.displacementScale=e.displacementScale,this.displacementBias=e.displacementBias,this.wireframe=e.wireframe,this.wireframeLinewidth=e.wireframeLinewidth,this}},Tc=class extends Nn{constructor(e){super(),this.isMeshDistanceMaterial=!0,this.type="MeshDistanceMaterial",this.map=null,this.alphaMap=null,this.displacementMap=null,this.displacementScale=1,this.displacementBias=0,this.setValues(e)}copy(e){return super.copy(e),this.map=e.map,this.alphaMap=e.alphaMap,this.displacementMap=e.displacementMap,this.displacementScale=e.displacementScale,this.displacementBias=e.displacementBias,this}};function bs(i,e){return!i||i.constructor===e?i:typeof e.BYTES_PER_ELEMENT=="number"?new e(i):Array.prototype.slice.call(i)}function sc(i){return i!==void 0&&i.inTangents!==void 0&&i.outTangents!==void 0}function ug(i){function e(s,r){return i[s]-i[r]}let t=i.length,n=new Array(t);for(let s=0;s!==t;++s)n[s]=s;return n.sort(e),n}function wf(i,e,t){let n=i.length,s=new i.constructor(n);for(let r=0,a=0;a!==n;++r){let o=t[r]*e;for(let c=0;c!==e;++c)s[a++]=i[o+c]}return s}function dg(i,e,t,n){let s=1,r=i[0];for(;r!==void 0&&r[n]===void 0;)r=i[s++];if(r===void 0)return;let a=r[n];if(a!==void 0)if(Array.isArray(a))do a=r[n],a!==void 0&&(e.push(r.time),t.push(...a)),r=i[s++];while(r!==void 0);else if(a.toArray!==void 0)do a=r[n],a!==void 0&&(e.push(r.time),a.toArray(t,t.length)),r=i[s++];while(r!==void 0);else do a=r[n],a!==void 0&&(e.push(r.time),t.push(a)),r=i[s++];while(r!==void 0)}var Ni=class{constructor(e,t,n,s){this.parameterPositions=e,this._cachedIndex=0,this.resultBuffer=s!==void 0?s:new t.constructor(n),this.sampleValues=t,this.valueSize=n,this.settings=null,this.DefaultSettings_={}}evaluate(e){let t=this.parameterPositions,n=this._cachedIndex,s=t[n],r=t[n-1];n:{e:{let a;t:{i:if(!(e<s)){for(let o=n+2;;){if(s===void 0){if(e<r)break i;return n=t.length,this._cachedIndex=n,this.copySampleValue_(n-1)}if(n===o)break;if(r=s,s=t[++n],e<s)break e}a=t.length;break t}if(!(e>=r)){let o=t[1];e<o&&(n=2,r=o);for(let c=n-2;;){if(r===void 0)return this._cachedIndex=0,this.copySampleValue_(0);if(n===c)break;if(s=r,r=t[--n-1],e>=r)break e}a=n,n=0;break t}break n}for(;n<a;){let o=n+a>>>1;e<t[o]?a=o:n=o+1}if(s=t[n],r=t[n-1],r===void 0)return this._cachedIndex=0,this.copySampleValue_(0);if(s===void 0)return n=t.length,this._cachedIndex=n,this.copySampleValue_(n-1)}this._cachedIndex=n,this.intervalChanged_(n,r,s)}return this.interpolate_(n,r,e,s)}getSettings_(){return this.settings||this.DefaultSettings_}copySampleValue_(e){let t=this.resultBuffer,n=this.sampleValues,s=this.valueSize,r=e*s;for(let a=0;a!==s;++a)t[a]=n[r+a];return t}interpolate_(){throw new Error("THREE.Interpolant: Call to abstract method.")}intervalChanged_(){}},wc=class extends Ni{constructor(e,t,n,s){super(e,t,n,s),this._weightPrev=-0,this._offsetPrev=-0,this._weightNext=-0,this._offsetNext=-0,this.DefaultSettings_={endingStart:Ih,endingEnd:Ih}}intervalChanged_(e,t,n){let s=this.parameterPositions,r=e-2,a=e+1,o=s[r],c=s[a];if(o===void 0)switch(this.getSettings_().endingStart){case Lh:r=e,o=2*t-n;break;case Dh:r=s.length-2,o=t+s[r]-s[r+1];break;default:r=e,o=n}if(c===void 0)switch(this.getSettings_().endingEnd){case Lh:a=e,c=2*n-t;break;case Dh:a=1,c=n+s[1]-s[0];break;default:a=e-1,c=t}let l=(n-t)*.5,h=this.valueSize;this._weightPrev=l/(t-o),this._weightNext=l/(c-n),this._offsetPrev=r*h,this._offsetNext=a*h}interpolate_(e,t,n,s){let r=this.resultBuffer,a=this.sampleValues,o=this.valueSize,c=e*o,l=c-o,h=this._offsetPrev,u=this._offsetNext,d=this._weightPrev,f=this._weightNext,m=(n-t)/(s-t),b=m*m,g=b*m,p=-d*g+2*d*b-d*m,_=(1+d)*g+(-1.5-2*d)*b+(-.5+d)*m+1,S=(-1-f)*g+(1.5+f)*b+.5*m,x=f*g-f*b;for(let M=0;M!==o;++M)r[M]=p*a[h+M]+_*a[l+M]+S*a[c+M]+x*a[u+M];return r}},Ac=class extends Ni{constructor(e,t,n,s){super(e,t,n,s)}interpolate_(e,t,n,s){let r=this.resultBuffer,a=this.sampleValues,o=this.valueSize,c=e*o,l=c-o,h=(n-t)/(s-t),u=1-h;for(let d=0;d!==o;++d)r[d]=a[l+d]*u+a[c+d]*h;return r}},Rc=class extends Ni{constructor(e,t,n,s){super(e,t,n,s)}interpolate_(e){return this.copySampleValue_(e-1)}},Cc=class extends Ni{interpolate_(e,t,n,s){let r=this.resultBuffer,a=this.sampleValues,o=this.valueSize,c=e*o,l=c-o,h=this.inTangents,u=this.outTangents;if(!h||!u){let m=(n-t)/(s-t),b=1-m;for(let g=0;g!==o;++g)r[g]=a[l+g]*b+a[c+g]*m;return r}let d=o*2,f=e-1;for(let m=0;m!==o;++m){let b=a[l+m],g=a[c+m],p=f*d+m*2,_=u[p],S=u[p+1],x=e*d+m*2,M=h[x],E=h[x+1],A=pg(n,t,_,M,s);r[m]=Ep(A,b,S,E,g)}return r}};function Ep(i,e,t,n,s){let r=1-i;return r*r*r*e+3*r*r*i*t+3*r*i*i*n+i*i*i*s}function fg(i,e,t,n,s){let r=1-i;return 3*r*r*(t-e)+6*r*i*(n-t)+3*i*i*(s-n)}function pg(i,e,t,n,s){let r=(i-e)/(s-e);for(let a=0;a<8;a++){let o=Ep(r,e,t,n,s)-i;if(Math.abs(o)<1e-10)break;let c=fg(r,e,t,n,s);if(Math.abs(c)<1e-10)break;r=Math.max(0,Math.min(1,r-o/c))}return r}var Fn=class{constructor(e,t,n,s){if(e===void 0)throw new Error("THREE.KeyframeTrack: track name is undefined");if(t===void 0||t.length===0)throw new Error("THREE.KeyframeTrack: no keyframes in track named "+e);this.name=e,this.times=bs(t,this.TimeBufferType),this.values=bs(n,this.ValueBufferType),this.setInterpolation(s||this.DefaultInterpolation)}static toJSON(e){let t=e.constructor,n;if(t.toJSON!==this.toJSON)n=t.toJSON(e);else{n={name:e.name,times:bs(e.times,Array),values:bs(e.values,Array)};let s=e.getInterpolation();s!==e.DefaultInterpolation&&(n.interpolation=s),sc(e.settings)&&(n.settings={inTangents:bs(e.settings.inTangents,Array),outTangents:bs(e.settings.outTangents,Array)})}return n.type=e.ValueTypeName,n}InterpolantFactoryMethodDiscrete(e){return new Rc(this.times,this.values,this.getValueSize(),e)}InterpolantFactoryMethodLinear(e){return new Ac(this.times,this.values,this.getValueSize(),e)}InterpolantFactoryMethodSmooth(e){return new wc(this.times,this.values,this.getValueSize(),e)}InterpolantFactoryMethodBezier(e){let t=new Cc(this.times,this.values,this.getValueSize(),e);return this.settings&&(t.inTangents=this.settings.inTangents,t.outTangents=this.settings.outTangents),t}setInterpolation(e){let t;switch(e){case Vs:t=this.InterpolantFactoryMethodDiscrete;break;case Ws:t=this.InterpolantFactoryMethodLinear;break;case nc:t=this.InterpolantFactoryMethodSmooth;break;case Ph:t=this.InterpolantFactoryMethodBezier;break}if(t===void 0){let n="unsupported interpolation for "+this.ValueTypeName+" keyframe track named "+this.name;if(this.createInterpolant===void 0)if(e!==this.DefaultInterpolation)this.setInterpolation(this.DefaultInterpolation);else throw new Error(n);return Ve("KeyframeTrack:",n),this}return this.createInterpolant=t,this}getInterpolation(){switch(this.createInterpolant){case this.InterpolantFactoryMethodDiscrete:return Vs;case this.InterpolantFactoryMethodLinear:return Ws;case this.InterpolantFactoryMethodSmooth:return nc;case this.InterpolantFactoryMethodBezier:return Ph}}getValueSize(){return this.values.length/this.times.length}shift(e){if(e!==0){let t=this.times;for(let n=0,s=t.length;n!==s;++n)t[n]+=e}return this}scale(e){if(e!==1){let t=this.times;for(let n=0,s=t.length;n!==s;++n)t[n]*=e;sc(this.settings)&&(Af(this.settings.inTangents,e),Af(this.settings.outTangents,e))}return this}trim(e,t){let n=this.times,s=n.length,r=0,a=s-1;for(;r!==s&&n[r]<e;)++r;for(;a!==-1&&n[a]>t;)--a;if(++a,r!==0||a!==s){r>=a&&(a=Math.max(a,1),r=a-1);let o=this.getValueSize();this.times=n.slice(r,a),this.values=this.values.slice(r*o,a*o)}return this}validate(){let e=!0,t=this.getValueSize();t-Math.floor(t)!==0&&(Ye("KeyframeTrack: Invalid value size in track.",this),e=!1);let n=this.times,s=this.values,r=n.length;r===0&&(Ye("KeyframeTrack: Track is empty.",this),e=!1);let a=null;for(let o=0;o!==r;o++){let c=n[o];if(typeof c=="number"&&isNaN(c)){Ye("KeyframeTrack: Time is not a valid number.",this,o,c),e=!1;break}if(a!==null&&a>c){Ye("KeyframeTrack: Out of order keys.",this,o,c,a),e=!1;break}a=c}if(s!==void 0&&Z0(s))for(let o=0,c=s.length;o!==c;++o){let l=s[o];if(isNaN(l)){Ye("KeyframeTrack: Value is not a valid number.",this,o,l),e=!1;break}}return e}optimize(){let e=this.times.slice(),t=this.values.slice(),n=this.getValueSize(),s=this.getInterpolation()===nc,r=e.length-1,a=1;for(let o=1;o<r;++o){let c=!1,l=e[o],h=e[o+1];if(l!==h&&(o!==1||l!==e[0]))if(s)c=!0;else{let u=o*n,d=u-n,f=u+n;for(let m=0;m!==n;++m){let b=t[u+m];if(b!==t[d+m]||b!==t[f+m]){c=!0;break}}}if(c){if(o!==a){e[a]=e[o];let u=o*n,d=a*n;for(let f=0;f!==n;++f)t[d+f]=t[u+f]}++a}}if(r>0){e[a]=e[r];for(let o=r*n,c=a*n,l=0;l!==n;++l)t[c+l]=t[o+l];++a}return a!==e.length?(this.times=e.slice(0,a),this.values=t.slice(0,a*n)):(this.times=e,this.values=t),this}clone(){let e=this.times.slice(),t=this.values.slice(),n=this.constructor,s=new n(this.name,e,t);return s.createInterpolant=this.createInterpolant,sc(this.settings)&&(s.settings={inTangents:this.settings.inTangents.slice(),outTangents:this.settings.outTangents.slice()}),s}};function Af(i,e){for(let t=0,n=i.length;t!==n;t+=2)i[t]*=e}Fn.prototype.ValueTypeName="";Fn.prototype.TimeBufferType=Float32Array;Fn.prototype.ValueBufferType=Float32Array;Fn.prototype.DefaultInterpolation=Ws;var Qi=class extends Fn{constructor(e,t,n){super(e,t,n)}};Qi.prototype.ValueTypeName="bool";Qi.prototype.ValueBufferType=Array;Qi.prototype.DefaultInterpolation=Vs;Qi.prototype.InterpolantFactoryMethodLinear=void 0;Qi.prototype.InterpolantFactoryMethodSmooth=void 0;var ja=class extends Fn{constructor(e,t,n,s){super(e,t,n,s)}};ja.prototype.ValueTypeName="color";var es=class extends Fn{constructor(e,t,n,s){super(e,t,n,s)}};es.prototype.ValueTypeName="number";var Pc=class extends Ni{constructor(e,t,n,s){super(e,t,n,s)}interpolate_(e,t,n,s){let r=this.resultBuffer,a=this.sampleValues,o=this.valueSize,c=(n-t)/(s-t),l=e*o;for(let h=l+o;l!==h;l+=4)ln.slerpFlat(r,0,a,l-o,a,l,c);return r}},ts=class extends Fn{constructor(e,t,n,s){super(e,t,n,s)}InterpolantFactoryMethodLinear(e){return new Pc(this.times,this.values,this.getValueSize(),e)}};ts.prototype.ValueTypeName="quaternion";ts.prototype.InterpolantFactoryMethodSmooth=void 0;var ns=class extends Fn{constructor(e,t,n){super(e,t,n)}};ns.prototype.ValueTypeName="string";ns.prototype.ValueBufferType=Array;ns.prototype.DefaultInterpolation=Vs;ns.prototype.InterpolantFactoryMethodLinear=void 0;ns.prototype.InterpolantFactoryMethodSmooth=void 0;var Ss=class extends Fn{constructor(e,t,n,s){super(e,t,n,s)}};Ss.prototype.ValueTypeName="vector";var Ka=class{constructor(e="",t=-1,n=[],s=sp){this.name=e,this.tracks=n,this.duration=t,this.blendMode=s,this.uuid=ii(),this.userData={},this.duration<0&&this.resetDuration()}static parse(e){let t=[],n=e.tracks,s=1/(e.fps||1);for(let a=0,o=n.length;a!==o;++a)t.push(gg(n[a]).scale(s));let r=new this(e.name,e.duration,t,e.blendMode);return r.uuid=e.uuid,r.userData=JSON.parse(e.userData||"{}"),r}static toJSON(e){let t=[],n=e.tracks,s={name:e.name,duration:e.duration,tracks:t,uuid:e.uuid,blendMode:e.blendMode,userData:JSON.stringify(e.userData)};for(let r=0,a=n.length;r!==a;++r)t.push(Fn.toJSON(n[r]));return s}static CreateFromMorphTargetSequence(e,t,n,s){let r=t.length,a=[];for(let o=0;o<r;o++){let c=[],l=[];c.push((o+r-1)%r,o,(o+1)%r),l.push(0,1,0);let h=ug(c);c=wf(c,1,h),l=wf(l,1,h),!s&&c[0]===0&&(c.push(r),l.push(l[0])),a.push(new es(".morphTargetInfluences["+t[o].name+"]",c,l).scale(1/n))}return new this(e,-1,a)}static findByName(e,t){let n=e;if(!Array.isArray(e)){let s=e;n=s.geometry&&s.geometry.animations||s.animations}for(let s=0;s<n.length;s++)if(n[s].name===t)return n[s];return null}static CreateClipsFromMorphTargetSequences(e,t,n){let s={},r=/^([\w-]*?)([\d]+)$/;for(let o=0,c=e.length;o<c;o++){let l=e[o],h=l.name.match(r);if(h&&h.length>1){let u=h[1],d=s[u];d||(s[u]=d=[]),d.push(l)}}let a=[];for(let o in s)a.push(this.CreateFromMorphTargetSequence(o,s[o],t,n));return a}resetDuration(){let e=this.tracks,t=0;for(let n=0,s=e.length;n!==s;++n){let r=this.tracks[n];t=Math.max(t,r.times[r.times.length-1])}return this.duration=t,this}trim(){for(let e=0;e<this.tracks.length;e++)this.tracks[e].trim(0,this.duration);return this}validate(){let e=!0;for(let t=0;t<this.tracks.length;t++)e=e&&this.tracks[t].validate();return e}optimize(){for(let e=0;e<this.tracks.length;e++)this.tracks[e].optimize();return this}clone(){let e=[];for(let n=0;n<this.tracks.length;n++)e.push(this.tracks[n].clone());let t=new this.constructor(this.name,this.duration,e,this.blendMode);return t.userData=JSON.parse(JSON.stringify(this.userData)),t}toJSON(){return this.constructor.toJSON(this)}};function mg(i){switch(i.toLowerCase()){case"scalar":case"double":case"float":case"number":case"integer":return es;case"vector":case"vector2":case"vector3":case"vector4":return Ss;case"color":return ja;case"quaternion":return ts;case"bool":case"boolean":return Qi;case"string":return ns}throw new Error("THREE.KeyframeTrack: Unsupported typeName: "+i)}function gg(i){if(i.type===void 0)throw new Error("THREE.KeyframeTrack: track type undefined, can not parse");let e=mg(i.type);if(i.times===void 0){let n=[],s=[];dg(i.keys,n,s,"value"),i.times=n,i.values=s}let t;return e.parse!==void 0?t=e.parse(i):t=new e(i.name,i.times,i.values,i.interpolation),sc(i.settings)&&(t.settings={inTangents:bs(i.settings.inTangents,Float32Array),outTangents:bs(i.settings.outTangents,Float32Array)}),t}var Ci={enabled:!1,files:{},add:function(i,e){this.enabled!==!1&&(Rf(i)||(this.files[i]=e))},get:function(i){if(this.enabled!==!1&&!Rf(i))return this.files[i]},remove:function(i){delete this.files[i]},clear:function(){this.files={}}};function Rf(i){try{let e=i.slice(i.indexOf(":")+1);return new URL(e).protocol==="blob:"}catch{return!1}}var Ic=class{constructor(e,t,n){let s=this,r=!1,a=0,o=0,c,l=[];this.onStart=void 0,this.onLoad=e,this.onProgress=t,this.onError=n,this._abortController=null,this.itemStart=function(h){o++,r===!1&&s.onStart!==void 0&&s.onStart(h,a,o),r=!0},this.itemEnd=function(h){a++,s.onProgress!==void 0&&s.onProgress(h,a,o),a===o&&(r=!1,s.onLoad!==void 0&&s.onLoad())},this.itemError=function(h){s.onError!==void 0&&s.onError(h)},this.resolveURL=function(h){return h=h.normalize("NFC"),c?c(h):h},this.setURLModifier=function(h){return c=h,this},this.addHandler=function(h,u){return l.push(h,u),this},this.removeHandler=function(h){let u=l.indexOf(h);return u!==-1&&l.splice(u,2),this},this.getHandler=function(h){for(let u=0,d=l.length;u<d;u+=2){let f=l[u],m=l[u+1];if(f.global&&(f.lastIndex=0),f.test(h))return m}return null},this.abort=function(){return this.abortController.abort(),this._abortController=null,this}}get abortController(){return this._abortController||(this._abortController=new AbortController),this._abortController}},Tp=new Ic,Fi=class{constructor(e){this.manager=e!==void 0?e:Tp,this.crossOrigin="anonymous",this.withCredentials=!1,this.path="",this.resourcePath="",this.requestHeader={},typeof __THREE_DEVTOOLS__<"u"&&__THREE_DEVTOOLS__.dispatchEvent(new CustomEvent("observe",{detail:this}))}load(){}loadAsync(e,t){let n=this;return new Promise(function(s,r){n.load(e,s,t,r)})}parse(){}setCrossOrigin(e){return this.crossOrigin=e,this}setWithCredentials(e){return this.withCredentials=e,this}setPath(e){return this.path=e,this}setResourcePath(e){return this.resourcePath=e,this}setRequestHeader(e){return this.requestHeader=e,this}abort(){return this}};Fi.DEFAULT_MATERIAL_NAME="__DEFAULT";var Yi={},Bh=class extends Error{constructor(e,t){super(e),this.response=t}},Wr=class extends Fi{constructor(e){super(e),this.mimeType="",this.responseType="",this._abortController=new AbortController}load(e,t,n,s){e===void 0&&(e=""),this.path!==void 0&&(e=this.path+e),e=this.manager.resolveURL(e);let r=Ci.get(`file:${e}`);if(r!==void 0){this.manager.itemStart(e),setTimeout(()=>{t&&t(r),this.manager.itemEnd(e)},0);return}if(Yi[e]!==void 0){Yi[e].push({onLoad:t,onProgress:n,onError:s});return}Yi[e]=[],Yi[e].push({onLoad:t,onProgress:n,onError:s});let a=new Request(e,{headers:new Headers(this.requestHeader),credentials:this.withCredentials?"include":"same-origin",signal:typeof AbortSignal.any=="function"?AbortSignal.any([this._abortController.signal,this.manager.abortController.signal]):this._abortController.signal}),o=this.mimeType,c=this.responseType;fetch(a).then(l=>{if(l.status===200||l.status===0){if(l.status===0&&Ve("FileLoader: HTTP Status 0 received."),typeof ReadableStream>"u"||l.body===void 0||l.body.getReader===void 0)return l;let h=Yi[e],u=l.body.getReader(),d=l.headers.get("X-File-Size")||l.headers.get("Content-Length"),f=d?parseInt(d):0,m=f!==0,b=0,g=new ReadableStream({start(p){_();function _(){u.read().then(({done:S,value:x})=>{if(S)p.close();else{b+=x.byteLength;let M=new ProgressEvent("progress",{lengthComputable:m,loaded:b,total:f});for(let E=0,A=h.length;E<A;E++){let y=h[E];y.onProgress&&y.onProgress(M)}p.enqueue(x),_()}},S=>{p.error(S)})}}});return new Response(g)}else throw new Bh(`fetch for "${l.url}" responded with ${l.status}: ${l.statusText}`,l)}).then(l=>{switch(c){case"arraybuffer":return l.arrayBuffer();case"blob":return l.blob();case"document":return l.text().then(h=>new DOMParser().parseFromString(h,o));case"json":return l.json();default:if(o==="")return l.text();{let u=/charset="?([^;"\s]*)"?/i.exec(o),d=u&&u[1]?u[1].toLowerCase():void 0,f=new TextDecoder(d);return l.arrayBuffer().then(m=>f.decode(m))}}}).then(l=>{Ci.add(`file:${e}`,l);let h=Yi[e];delete Yi[e];for(let u=0,d=h.length;u<d;u++){let f=h[u];f.onLoad&&f.onLoad(l)}}).catch(l=>{let h=Yi[e];if(h===void 0)throw this.manager.itemError(e),l;delete Yi[e];for(let u=0,d=h.length;u<d;u++){let f=h[u];f.onError&&f.onError(l)}this.manager.itemError(e)}).finally(()=>{this.manager.itemEnd(e)}),this.manager.itemStart(e)}setResponseType(e){return this.responseType=e,this}setMimeType(e){return this.mimeType=e,this}abort(){return this._abortController.abort(),this._abortController=new AbortController,this}};var Sr=new WeakMap,Lc=class extends Fi{constructor(e){super(e)}load(e,t,n,s){this.path!==void 0&&(e=this.path+e),e=this.manager.resolveURL(e);let r=this,a=Ci.get(`image:${e}`);if(a!==void 0){if(a.complete===!0)r.manager.itemStart(e),setTimeout(function(){t&&t(a),r.manager.itemEnd(e)},0);else{let u=Sr.get(a);u===void 0&&(u=[],Sr.set(a,u)),u.push({onLoad:t,onError:s})}return a}let o=Pr("img");function c(){h(),t&&t(this);let u=Sr.get(this)||[];for(let d=0;d<u.length;d++){let f=u[d];f.onLoad&&f.onLoad(this)}Sr.delete(this),r.manager.itemEnd(e)}function l(u){h(),s&&s(u),Ci.remove(`image:${e}`);let d=Sr.get(this)||[];for(let f=0;f<d.length;f++){let m=d[f];m.onError&&m.onError(u)}Sr.delete(this),r.manager.itemError(e),r.manager.itemEnd(e)}function h(){o.removeEventListener("load",c,!1),o.removeEventListener("error",l,!1)}return o.addEventListener("load",c,!1),o.addEventListener("error",l,!1),e.slice(0,5)!=="data:"&&this.crossOrigin!==void 0&&(o.crossOrigin=this.crossOrigin),Ci.add(`image:${e}`,o),r.manager.itemStart(e),o.src=e,o}};var Js=class extends Fi{constructor(e){super(e)}load(e,t,n,s){let r=new hn,a=new Lc(this.manager);return a.setCrossOrigin(this.crossOrigin),a.setPath(this.path),a.load(e,function(o){r.image=o,r.needsUpdate=!0,t!==void 0&&t(r)},n,s),r}},Zs=class extends At{constructor(e,t=1){super(),this.isLight=!0,this.type="Light",this.color=new Be(e),this.intensity=t}copy(e,t){return super.copy(e,t),this.color.copy(e.color),this.intensity=e.intensity,this}toJSON(e){let t=super.toJSON(e);return t.object.color=this.color.getHex(),t.object.intensity=this.intensity,t}},$s=class extends Zs{constructor(e,t,n){super(e,n),this.isHemisphereLight=!0,this.type="HemisphereLight",this.position.copy(At.DEFAULT_UP),this.updateMatrix(),this.groundColor=new Be(t)}copy(e,t){return super.copy(e,t),this.groundColor.copy(e.groundColor),this}toJSON(e){let t=super.toJSON(e);return t.object.groundColor=this.groundColor.getHex(),t}},Ah=new Ze,Cf=new P,Pf=new P,qr=class{constructor(e){this.camera=e,this.intensity=1,this.bias=0,this.biasNode=null,this.normalBias=0,this.radius=1,this.blurSamples=8,this.mapSize=new ae(512,512),this.mapType=Un,this.map=null,this.mapPass=null,this.matrix=new Ze,this.autoUpdate=!0,this.needsUpdate=!1,this._frustum=new kr,this._frameExtents=new ae(1,1),this._viewportCount=1,this._viewports=[new Pt(0,0,1,1)]}getViewportCount(){return this._viewportCount}getCamera(){return this.camera}getFrustum(){return this._frustum}updateMatrices(e){let t=this.camera;Cf.setFromMatrixPosition(e.matrixWorld),t.position.copy(Cf),Pf.setFromMatrixPosition(e.target.matrixWorld),t.lookAt(Pf),t.updateMatrixWorld(),this._updateMatrix(t,this.matrix,this._frustum)}_updateMatrix(e,t,n,s){Ah.multiplyMatrices(e.projectionMatrix,e.matrixWorldInverse),n.setFromProjectionMatrix(Ah,e.coordinateSystem,e.reversedDepth);let r=this._frameExtents,a=s?s.z/r.x:1,o=s?s.w/r.y:1,c=s?s.x/r.x:0,l=s?s.y/r.y:0;e.coordinateSystem===Cr||e.reversedDepth?t.set(.5*a,0,0,.5*a+c,0,.5*o,0,.5*o+l,0,0,1,0,0,0,0,1):t.set(.5*a,0,0,.5*a+c,0,.5*o,0,.5*o+l,0,0,.5,.5,0,0,0,1),t.multiply(Ah)}getViewport(e){return this._viewports[e]}getFrameExtents(){return this._frameExtents}dispose(){this.map&&this.map.dispose(),this.mapPass&&this.mapPass.dispose()}copy(e){return this.camera=e.camera.clone(),this.intensity=e.intensity,this.bias=e.bias,this.radius=e.radius,this.autoUpdate=e.autoUpdate,this.needsUpdate=e.needsUpdate,this.normalBias=e.normalBias,this.blurSamples=e.blurSamples,this.mapSize.copy(e.mapSize),this.biasNode=e.biasNode,this}clone(){return new this.constructor().copy(this)}toJSON(){let e={};return e.intensity=this.intensity,e.bias=this.bias,e.normalBias=this.normalBias,e.radius=this.radius,e.blurSamples=this.blurSamples,e.mapSize=this.mapSize.toArray(),e.camera=this.camera.toJSON(!1).object,delete e.camera.matrix,e}},ec=new P,tc=new ln,Ai=new P,Ya=class extends At{constructor(){super(),this.isCamera=!0,this.type="Camera",this.matrixWorldInverse=new Ze,this.projectionMatrix=new Ze,this.projectionMatrixInverse=new Ze,this.coordinateSystem=xi,this._reversedDepth=!1}get reversedDepth(){return this._reversedDepth}copy(e,t){return super.copy(e,t),this.matrixWorldInverse.copy(e.matrixWorldInverse),this.projectionMatrix.copy(e.projectionMatrix),this.projectionMatrixInverse.copy(e.projectionMatrixInverse),this.coordinateSystem=e.coordinateSystem,this}getWorldDirection(e){return super.getWorldDirection(e).negate()}updateMatrixWorld(e){super.updateMatrixWorld(e),this.matrixWorld.decompose(ec,tc,Ai),Ai.x===1&&Ai.y===1&&Ai.z===1?this.matrixWorldInverse.copy(this.matrixWorld).invert():this.matrixWorldInverse.compose(ec,tc,Ai.set(1,1,1)).invert()}updateWorldMatrix(e,t,n=!1){super.updateWorldMatrix(e,t,n),this.matrixWorld.decompose(ec,tc,Ai),Ai.x===1&&Ai.y===1&&Ai.z===1?this.matrixWorldInverse.copy(this.matrixWorld).invert():this.matrixWorldInverse.compose(ec,tc,Ai.set(1,1,1)).invert()}clone(){return new this.constructor().copy(this)}},gs=new P,If=new ae,Lf=new ae,Yt=class extends Ya{constructor(e=50,t=1,n=.1,s=2e3){super(),this.isPerspectiveCamera=!0,this.type="PerspectiveCamera",this.fov=e,this.zoom=1,this.near=n,this.far=s,this.focus=10,this.aspect=t,this.view=null,this.filmGauge=35,this.filmOffset=0,this.updateProjectionMatrix()}copy(e,t){return super.copy(e,t),this.fov=e.fov,this.zoom=e.zoom,this.near=e.near,this.far=e.far,this.focus=e.focus,this.aspect=e.aspect,this.view=e.view===null?null:Object.assign({},e.view),this.filmGauge=e.filmGauge,this.filmOffset=e.filmOffset,this}setFocalLength(e){let t=.5*this.getFilmHeight()/e;this.fov=qs*2*Math.atan(t),this.updateProjectionMatrix()}getFocalLength(){let e=Math.tan(ya*.5*this.fov);return .5*this.getFilmHeight()/e}getEffectiveFOV(){return qs*2*Math.atan(Math.tan(ya*.5*this.fov)/this.zoom)}getFilmWidth(){return this.filmGauge*Math.min(this.aspect,1)}getFilmHeight(){return this.filmGauge/Math.max(this.aspect,1)}getViewBounds(e,t,n){gs.set(-1,-1,.5).applyMatrix4(this.projectionMatrixInverse),t.set(gs.x,gs.y).multiplyScalar(-e/gs.z),gs.set(1,1,.5).applyMatrix4(this.projectionMatrixInverse),n.set(gs.x,gs.y).multiplyScalar(-e/gs.z)}getViewSize(e,t){return this.getViewBounds(e,If,Lf),t.subVectors(Lf,If)}setViewOffset(e,t,n,s,r,a){this.aspect=e/t,this.view===null&&(this.view={enabled:!0,fullWidth:1,fullHeight:1,offsetX:0,offsetY:0,width:1,height:1}),this.view.enabled=!0,this.view.fullWidth=e,this.view.fullHeight=t,this.view.offsetX=n,this.view.offsetY=s,this.view.width=r,this.view.height=a,this.updateProjectionMatrix()}clearViewOffset(){this.view!==null&&(this.view.enabled=!1),this.updateProjectionMatrix()}updateProjectionMatrix(){let e=this.near,t=e*Math.tan(ya*.5*this.fov)/this.zoom,n=2*t,s=this.aspect*n,r=-.5*s,a=this.view;if(this.view!==null&&this.view.enabled){let c=a.fullWidth,l=a.fullHeight;r+=a.offsetX*s/c,t-=a.offsetY*n/l,s*=a.width/c,n*=a.height/l}let o=this.filmOffset;o!==0&&(r+=e*o/this.getFilmWidth()),this.projectionMatrix.makePerspective(r,r+s,t,t-n,e,this.far,this.coordinateSystem,this.reversedDepth),this.projectionMatrixInverse.copy(this.projectionMatrix).invert()}toJSON(e){let t=super.toJSON(e);return t.object.fov=this.fov,t.object.zoom=this.zoom,t.object.near=this.near,t.object.far=this.far,t.object.focus=this.focus,t.object.aspect=this.aspect,this.view!==null&&(t.object.view=Object.assign({},this.view)),t.object.filmGauge=this.filmGauge,t.object.filmOffset=this.filmOffset,t}},kh=class extends qr{constructor(){super(new Yt(50,1,.5,500)),this.isSpotLightShadow=!0,this.focus=1,this.aspect=1}updateMatrices(e){let t=this.camera,n=qs*2*e.angle*this.focus,s=this.mapSize.width/this.mapSize.height*this.aspect,r=e.distance||t.far;(n!==t.fov||s!==t.aspect||r!==t.far)&&(t.fov=n,t.aspect=s,t.far=r,t.updateProjectionMatrix()),super.updateMatrices(e)}copy(e){return super.copy(e),this.focus=e.focus,this.aspect=e.aspect,this}toJSON(){let e=super.toJSON();return e.focus=this.focus,e.aspect=this.aspect,e}},Ja=class extends Zs{constructor(e,t,n=0,s=Math.PI/3,r=0,a=2){super(e,t),this.isSpotLight=!0,this.type="SpotLight",this.position.copy(At.DEFAULT_UP),this.updateMatrix(),this.target=new At,this.distance=n,this.angle=s,this.penumbra=r,this.decay=a,this.map=null,this.shadow=new kh}get power(){return this.intensity*Math.PI}set power(e){this.intensity=e/Math.PI}dispose(){super.dispose(),this.shadow.dispose()}copy(e,t){return super.copy(e,t),this.distance=e.distance,this.angle=e.angle,this.penumbra=e.penumbra,this.decay=e.decay,this.target=e.target.clone(),this.map=e.map,this.shadow=e.shadow.clone(),this}toJSON(e){let t=super.toJSON(e);return t.object.distance=this.distance,t.object.angle=this.angle,t.object.decay=this.decay,t.object.penumbra=this.penumbra,t.object.target=this.target.uuid,this.map&&this.map.isTexture&&(t.object.map=this.map.toJSON(e).uuid),t.object.shadow=this.shadow.toJSON(),t}},zh=class extends qr{constructor(){super(new Yt(90,1,.5,500)),this.isPointLightShadow=!0}},Qs=class extends Zs{constructor(e,t,n=0,s=2){super(e,t),this.isPointLight=!0,this.type="PointLight",this.distance=n,this.decay=s,this.shadow=new zh}get power(){return this.intensity*4*Math.PI}set power(e){this.intensity=e/(4*Math.PI)}dispose(){super.dispose(),this.shadow.dispose()}copy(e,t){return super.copy(e,t),this.distance=e.distance,this.decay=e.decay,this.shadow=e.shadow.clone(),this}toJSON(e){let t=super.toJSON(e);return t.object.distance=this.distance,t.object.decay=this.decay,t.object.shadow=this.shadow.toJSON(),t}},Es=class extends Ya{constructor(e=-1,t=1,n=1,s=-1,r=.1,a=2e3){super(),this.isOrthographicCamera=!0,this.type="OrthographicCamera",this.zoom=1,this.view=null,this.left=e,this.right=t,this.top=n,this.bottom=s,this.near=r,this.far=a,this.updateProjectionMatrix()}copy(e,t){return super.copy(e,t),this.left=e.left,this.right=e.right,this.top=e.top,this.bottom=e.bottom,this.near=e.near,this.far=e.far,this.zoom=e.zoom,this.view=e.view===null?null:Object.assign({},e.view),this}setViewOffset(e,t,n,s,r,a){this.view===null&&(this.view={enabled:!0,fullWidth:1,fullHeight:1,offsetX:0,offsetY:0,width:1,height:1}),this.view.enabled=!0,this.view.fullWidth=e,this.view.fullHeight=t,this.view.offsetX=n,this.view.offsetY=s,this.view.width=r,this.view.height=a,this.updateProjectionMatrix()}clearViewOffset(){this.view!==null&&(this.view.enabled=!1),this.updateProjectionMatrix()}updateProjectionMatrix(){let e=(this.right-this.left)/(2*this.zoom),t=(this.top-this.bottom)/(2*this.zoom),n=(this.right+this.left)/2,s=(this.top+this.bottom)/2,r=n-e,a=n+e,o=s+t,c=s-t;if(this.view!==null&&this.view.enabled){let l=(this.right-this.left)/this.view.fullWidth/this.zoom,h=(this.top-this.bottom)/this.view.fullHeight/this.zoom;r+=l*this.view.offsetX,a=r+l*this.view.width,o-=h*this.view.offsetY,c=o-h*this.view.height}this.projectionMatrix.makeOrthographic(r,a,o,c,this.near,this.far,this.coordinateSystem,this.reversedDepth),this.projectionMatrixInverse.copy(this.projectionMatrix).invert()}toJSON(e){let t=super.toJSON(e);return t.object.zoom=this.zoom,t.object.left=this.left,t.object.right=this.right,t.object.top=this.top,t.object.bottom=this.bottom,t.object.near=this.near,t.object.far=this.far,this.view!==null&&(t.object.view=Object.assign({},this.view)),t}},Hh=class extends qr{constructor(){super(new Es(-5,5,5,-5,.5,500)),this.isDirectionalLightShadow=!0}},yi=class extends Zs{constructor(e,t){super(e,t),this.isDirectionalLight=!0,this.type="DirectionalLight",this.position.copy(At.DEFAULT_UP),this.updateMatrix(),this.target=new At,this.shadow=new Hh}dispose(){super.dispose(),this.shadow.dispose()}copy(e){return super.copy(e),this.target=e.target.clone(),this.shadow=e.shadow.clone(),this}toJSON(e){let t=super.toJSON(e);return t.object.shadow=this.shadow.toJSON(),t.object.target=this.target.uuid,t}};var is=class{static extractUrlBase(e){let t=e.lastIndexOf("/");return t===-1?"./":e.slice(0,t+1)}static resolveURL(e,t){return typeof e!="string"||e===""?"":(/^https?:\/\//i.test(t)&&/^\//.test(e)&&(t=t.replace(/(^https?:\/\/[^\/]+).*/i,"$1")),/^(https?:)?\/\//i.test(e)||/^data:.*,.*$/i.test(e)||/^blob:.*$/i.test(e)?e:t+e)}};var Rh=new WeakMap,Za=class extends Fi{constructor(e){super(e),this.isImageBitmapLoader=!0,typeof createImageBitmap>"u"&&Ve("ImageBitmapLoader: createImageBitmap() not supported."),typeof fetch>"u"&&Ve("ImageBitmapLoader: fetch() not supported."),this.options={premultiplyAlpha:"none"},this._abortController=new AbortController}setOptions(e){return this.options=e,this}load(e,t,n,s){e===void 0&&(e=""),this.path!==void 0&&(e=this.path+e),e=this.manager.resolveURL(e);let r=this,a=Ci.get(`image-bitmap:${e}`);if(a!==void 0){if(r.manager.itemStart(e),a.then){a.then(l=>{Rh.has(a)===!0?(s&&s(Rh.get(a)),r.manager.itemError(e),r.manager.itemEnd(e)):(t&&t(l),r.manager.itemEnd(e))});return}setTimeout(function(){t&&t(a),r.manager.itemEnd(e)},0);return}let o={};o.credentials=this.crossOrigin==="anonymous"?"same-origin":"include",o.headers=this.requestHeader,o.signal=typeof AbortSignal.any=="function"?AbortSignal.any([this._abortController.signal,this.manager.abortController.signal]):this._abortController.signal;let c=fetch(e,o).then(function(l){return l.blob()}).then(function(l){return createImageBitmap(l,Object.assign({},r.options,{colorSpaceConversion:"none"}))}).then(function(l){return Ci.add(`image-bitmap:${e}`,l),t&&t(l),r.manager.itemEnd(e),l}).catch(function(l){s&&s(l),Rh.set(c,l),Ci.remove(`image-bitmap:${e}`),r.manager.itemError(e),r.manager.itemEnd(e)});Ci.add(`image-bitmap:${e}`,c),r.manager.itemStart(e)}abort(){return this._abortController.abort(),this._abortController=new AbortController,this}};var Er=-90,Tr=1,Dc=class extends At{constructor(e,t,n){super(),this.type="CubeCamera",this.renderTarget=n,this.coordinateSystem=null,this.activeMipmapLevel=0;let s=new Yt(Er,Tr,e,t);s.layers=this.layers,this.add(s);let r=new Yt(Er,Tr,e,t);r.layers=this.layers,this.add(r);let a=new Yt(Er,Tr,e,t);a.layers=this.layers,this.add(a);let o=new Yt(Er,Tr,e,t);o.layers=this.layers,this.add(o);let c=new Yt(Er,Tr,e,t);c.layers=this.layers,this.add(c);let l=new Yt(Er,Tr,e,t);l.layers=this.layers,this.add(l)}updateCoordinateSystem(){let e=this.coordinateSystem,t=this.children.concat(),[n,s,r,a,o,c]=t;for(let l of t)this.remove(l);if(e===xi)n.up.set(0,1,0),n.lookAt(1,0,0),s.up.set(0,1,0),s.lookAt(-1,0,0),r.up.set(0,0,-1),r.lookAt(0,1,0),a.up.set(0,0,1),a.lookAt(0,-1,0),o.up.set(0,1,0),o.lookAt(0,0,1),c.up.set(0,1,0),c.lookAt(0,0,-1);else if(e===Cr)n.up.set(0,-1,0),n.lookAt(-1,0,0),s.up.set(0,-1,0),s.lookAt(1,0,0),r.up.set(0,0,1),r.lookAt(0,1,0),a.up.set(0,0,-1),a.lookAt(0,-1,0),o.up.set(0,-1,0),o.lookAt(0,0,1),c.up.set(0,-1,0),c.lookAt(0,0,-1);else throw new Error("THREE.CubeCamera.updateCoordinateSystem(): Invalid coordinate system: "+e);for(let l of t)this.add(l),l.updateMatrixWorld()}update(e,t){this.parent===null&&this.updateMatrixWorld();let{renderTarget:n,activeMipmapLevel:s}=this;this.coordinateSystem!==e.coordinateSystem&&(this.coordinateSystem=e.coordinateSystem,this.updateCoordinateSystem());let[r,a,o,c,l,h]=this.children,u=e.getRenderTarget(),d=e.getActiveCubeFace(),f=e.getActiveMipmapLevel(),m=e.xr.enabled;e.xr.enabled=!1;let b=n.texture.generateMipmaps;n.texture.generateMipmaps=!1;let g=!1;e.isWebGLRenderer===!0?g=e.state.buffers.depth.getReversed():g=e.reversedDepthBuffer,e.setRenderTarget(n,0,s),g&&e.autoClear===!1&&e.clearDepth(),e.render(t,r),e.setRenderTarget(n,1,s),g&&e.autoClear===!1&&e.clearDepth(),e.render(t,a),e.setRenderTarget(n,2,s),g&&e.autoClear===!1&&e.clearDepth(),e.render(t,o),e.setRenderTarget(n,3,s),g&&e.autoClear===!1&&e.clearDepth(),e.render(t,c),e.setRenderTarget(n,4,s),g&&e.autoClear===!1&&e.clearDepth(),e.render(t,l),n.texture.generateMipmaps=b,e.setRenderTarget(n,5,s),g&&e.autoClear===!1&&e.clearDepth(),e.render(t,h),e.setRenderTarget(u,d,f),e.xr.enabled=m,n.texture.needsPMREMUpdate=!0}},Nc=class extends Yt{constructor(e=[]){super(),this.isArrayCamera=!0,this.isMultiViewCamera=!1,this.cameras=e}};var gu="\\[\\]\\.:\\/",bg=new RegExp("["+gu+"]","g"),bu="[^"+gu+"]",xg="[^"+gu.replace("\\.","")+"]",_g=/((?:WC+[\/:])*)/.source.replace("WC",bu),yg=/(WCOD+)?/.source.replace("WCOD",xg),vg=/(?:\.(WC+)(?:\[(.+)\])?)?/.source.replace("WC",bu),Mg=/\.(WC+)(?:\[(.+)\])?/.source.replace("WC",bu),Sg=new RegExp("^"+_g+yg+vg+Mg+"$"),Eg=["material","materials","bones","map"],Gh=class{constructor(e,t,n){let s=n||Ot.parseTrackName(t);this._targetGroup=e,this._bindings=e.subscribe_(t,s)}getValue(e,t){this.bind();let n=this._targetGroup.nCachedObjects_,s=this._bindings[n];s!==void 0&&s.getValue(e,t)}setValue(e,t){let n=this._bindings;for(let s=this._targetGroup.nCachedObjects_,r=n.length;s!==r;++s)n[s].setValue(e,t)}bind(){let e=this._bindings;for(let t=this._targetGroup.nCachedObjects_,n=e.length;t!==n;++t)e[t].bind()}unbind(){let e=this._bindings;for(let t=this._targetGroup.nCachedObjects_,n=e.length;t!==n;++t)e[t].unbind()}},Ot=class i{constructor(e,t,n){this.path=t,this.parsedPath=n||i.parseTrackName(t),this.node=i.findNode(e,this.parsedPath.nodeName),this.rootNode=e,this.getValue=this._getValue_unbound,this.setValue=this._setValue_unbound}static create(e,t,n){return e&&e.isAnimationObjectGroup?new i.Composite(e,t,n):new i(e,t,n)}static sanitizeNodeName(e){return e.replace(/\s/g,"_").replace(bg,"")}static parseTrackName(e){let t=Sg.exec(e);if(t===null)throw new Error("THREE.PropertyBinding: Cannot parse trackName: "+e);let n={nodeName:t[2],objectName:t[3],objectIndex:t[4],propertyName:t[5],propertyIndex:t[6]},s=n.nodeName&&n.nodeName.lastIndexOf(".");if(s!==void 0&&s!==-1){let r=n.nodeName.substring(s+1);Eg.indexOf(r)!==-1&&(n.nodeName=n.nodeName.substring(0,s),n.objectName=r)}if(n.propertyName===null||n.propertyName.length===0)throw new Error("THREE.PropertyBinding: can not parse propertyName from trackName: "+e);return n}static findNode(e,t){if(t===void 0||t===""||t==="."||t===-1||t===e.name||t===e.uuid)return e;if(e.skeleton){let n=e.skeleton.getBoneByName(t);if(n!==void 0)return n}if(e.children){let n=function(r){for(let a=0;a<r.length;a++){let o=r[a];if(o.name===t||o.uuid===t)return o;let c=n(o.children);if(c)return c}return null},s=n(e.children);if(s)return s}return null}_getValue_unavailable(){}_setValue_unavailable(){}_getValue_direct(e,t){e[t]=this.targetObject[this.propertyName]}_getValue_array(e,t){let n=this.resolvedProperty;for(let s=0,r=n.length;s!==r;++s)e[t++]=n[s]}_getValue_arrayElement(e,t){e[t]=this.resolvedProperty[this.propertyIndex]}_getValue_toArray(e,t){this.resolvedProperty.toArray(e,t)}_setValue_direct(e,t){this.targetObject[this.propertyName]=e[t]}_setValue_direct_setNeedsUpdate(e,t){this.targetObject[this.propertyName]=e[t],this.targetObject.needsUpdate=!0}_setValue_direct_setMatrixWorldNeedsUpdate(e,t){this.targetObject[this.propertyName]=e[t],this.targetObject.matrixWorldNeedsUpdate=!0}_setValue_array(e,t){let n=this.resolvedProperty;for(let s=0,r=n.length;s!==r;++s)n[s]=e[t++]}_setValue_array_setNeedsUpdate(e,t){let n=this.resolvedProperty;for(let s=0,r=n.length;s!==r;++s)n[s]=e[t++];this.targetObject.needsUpdate=!0}_setValue_array_setMatrixWorldNeedsUpdate(e,t){let n=this.resolvedProperty;for(let s=0,r=n.length;s!==r;++s)n[s]=e[t++];this.targetObject.matrixWorldNeedsUpdate=!0}_setValue_arrayElement(e,t){this.resolvedProperty[this.propertyIndex]=e[t]}_setValue_arrayElement_setNeedsUpdate(e,t){this.resolvedProperty[this.propertyIndex]=e[t],this.targetObject.needsUpdate=!0}_setValue_arrayElement_setMatrixWorldNeedsUpdate(e,t){this.resolvedProperty[this.propertyIndex]=e[t],this.targetObject.matrixWorldNeedsUpdate=!0}_setValue_fromArray(e,t){this.resolvedProperty.fromArray(e,t)}_setValue_fromArray_setNeedsUpdate(e,t){this.resolvedProperty.fromArray(e,t),this.targetObject.needsUpdate=!0}_setValue_fromArray_setMatrixWorldNeedsUpdate(e,t){this.resolvedProperty.fromArray(e,t),this.targetObject.matrixWorldNeedsUpdate=!0}_getValue_unbound(e,t){this.bind(),this.getValue(e,t)}_setValue_unbound(e,t){this.bind(),this.setValue(e,t)}bind(){let e=this.node,t=this.parsedPath,n=t.objectName,s=t.propertyName,r=t.propertyIndex;if(e||(e=i.findNode(this.rootNode,t.nodeName),this.node=e),this.getValue=this._getValue_unavailable,this.setValue=this._setValue_unavailable,!e){Ve("PropertyBinding: No target node found for track: "+this.path+".");return}if(n){let l=t.objectIndex;switch(n){case"materials":if(!e.material){Ye("PropertyBinding: Can not bind to material as node does not have a material.",this);return}if(!e.material.materials){Ye("PropertyBinding: Can not bind to material.materials as node.material does not have a materials array.",this);return}e=e.material.materials;break;case"bones":if(!e.skeleton){Ye("PropertyBinding: Can not bind to bones as node does not have a skeleton.",this);return}e=e.skeleton.bones;for(let h=0;h<e.length;h++)if(e[h].name===l){l=h;break}break;case"map":if("map"in e){e=e.map;break}if(!e.material){Ye("PropertyBinding: Can not bind to material as node does not have a material.",this);return}if(!e.material.map){Ye("PropertyBinding: Can not bind to material.map as node.material does not have a map.",this);return}e=e.material.map;break;default:if(e[n]===void 0){Ye("PropertyBinding: Can not bind to objectName of node undefined.",this);return}e=e[n]}if(l!==void 0){if(e[l]===void 0){Ye("PropertyBinding: Trying to bind to objectIndex of objectName, but is undefined.",this,e);return}e=e[l]}}let a=e[s];if(a===void 0){let l=t.nodeName;Ye("PropertyBinding: Trying to update property for track: "+l+"."+s+" but it wasn't found.",e);return}let o=this.Versioning.None;this.targetObject=e,e.isMaterial===!0?o=this.Versioning.NeedsUpdate:e.isObject3D===!0&&(o=this.Versioning.MatrixWorldNeedsUpdate);let c=this.BindingType.Direct;if(r!==void 0){if(s==="morphTargetInfluences"){if(!e.geometry){Ye("PropertyBinding: Can not bind to morphTargetInfluences because node does not have a geometry.",this);return}if(!e.geometry.morphAttributes){Ye("PropertyBinding: Can not bind to morphTargetInfluences because node does not have a geometry.morphAttributes.",this);return}e.morphTargetDictionary[r]!==void 0&&(r=e.morphTargetDictionary[r])}c=this.BindingType.ArrayElement,this.resolvedProperty=a,this.propertyIndex=r}else a.fromArray!==void 0&&a.toArray!==void 0?(c=this.BindingType.HasFromToArray,this.resolvedProperty=a):Array.isArray(a)?(c=this.BindingType.EntireArray,this.resolvedProperty=a):this.propertyName=s;this.getValue=this.GetterByBindingType[c],this.setValue=this.SetterByBindingTypeAndVersioning[c][o]}unbind(){this.node=null,this.getValue=this._getValue_unbound,this.setValue=this._setValue_unbound}};Ot.Composite=Gh;Ot.prototype.BindingType={Direct:0,EntireArray:1,ArrayElement:2,HasFromToArray:3};Ot.prototype.Versioning={None:0,NeedsUpdate:1,MatrixWorldNeedsUpdate:2};Ot.prototype.GetterByBindingType=[Ot.prototype._getValue_direct,Ot.prototype._getValue_array,Ot.prototype._getValue_arrayElement,Ot.prototype._getValue_toArray];Ot.prototype.SetterByBindingTypeAndVersioning=[[Ot.prototype._setValue_direct,Ot.prototype._setValue_direct_setNeedsUpdate,Ot.prototype._setValue_direct_setMatrixWorldNeedsUpdate],[Ot.prototype._setValue_array,Ot.prototype._setValue_array_setNeedsUpdate,Ot.prototype._setValue_array_setMatrixWorldNeedsUpdate],[Ot.prototype._setValue_arrayElement,Ot.prototype._setValue_arrayElement_setNeedsUpdate,Ot.prototype._setValue_arrayElement_setMatrixWorldNeedsUpdate],[Ot.prototype._setValue_fromArray,Ot.prototype._setValue_fromArray_setNeedsUpdate,Ot.prototype._setValue_fromArray_setMatrixWorldNeedsUpdate]];var Cv=new Float32Array(1);var Df=new Ze,ss=class{constructor(e,t,n=0,s=1/0){this.ray=new Li(e,t),this.near=n,this.far=s,this.camera=null,this.layers=new Dr,this.params={Mesh:{},Line:{threshold:1},LOD:{},Points:{threshold:1},Sprite:{}}}set(e,t){this.ray.set(e,t)}setFromCamera(e,t){t.isPerspectiveCamera?(this.ray.origin.setFromMatrixPosition(t.matrixWorld),this.ray.direction.set(e.x,e.y,.5).unproject(t).sub(this.ray.origin).normalize(),this.camera=t):t.isOrthographicCamera?(this.ray.origin.set(e.x,e.y,t.projectionMatrix.elements[14]).unproject(t),this.ray.direction.set(0,0,-1).transformDirection(t.matrixWorld),this.camera=t):Ye("Raycaster: Unsupported camera type: "+t.type)}setFromXRController(e){return Df.identity().extractRotation(e.matrixWorld),this.ray.origin.setFromMatrixPosition(e.matrixWorld),this.ray.direction.set(0,0,-1).applyMatrix4(Df),this}intersectObject(e,t=!0,n=[]){return Vh(e,this,n,t),n.sort(Nf),n}intersectObjects(e,t=!0,n=[]){for(let s=0,r=e.length;s<r;s++)Vh(e[s],this,n,t);return n.sort(Nf),n}};function Nf(i,e){return i.distance-e.distance}function Vh(i,e,t,n){let s=!0;if(i.layers.test(e.layers)&&i.raycast(e,t)===!1&&(s=!1),s===!0&&n===!0){let r=i.children;for(let a=0,o=r.length;a<o;a++)Vh(r[a],e,t,!0)}}var Ts=class{constructor(e=1,t=0,n=0){this.radius=e,this.phi=t,this.theta=n}set(e,t,n){return this.radius=e,this.phi=t,this.theta=n,this}copy(e){return this.radius=e.radius,this.phi=e.phi,this.theta=e.theta,this}makeSafe(){return this.phi=it(this.phi,1e-6,Math.PI-1e-6),this}setFromVector3(e){return this.setFromCartesianCoords(e.x,e.y,e.z)}setFromCartesianCoords(e,t,n){return this.radius=Math.sqrt(e*e+t*t+n*n),this.radius===0?(this.theta=0,this.phi=0):(this.theta=Math.atan2(e,n),this.phi=Math.acos(it(t/this.radius,-1,1))),this}clone(){return new this.constructor().copy(this)}};var Wh=class i{static{i.prototype.isMatrix2=!0}constructor(e,t,n,s){this.elements=[1,0,0,1],e!==void 0&&this.set(e,t,n,s)}identity(){return this.set(1,0,0,1),this}fromArray(e,t=0){for(let n=0;n<4;n++)this.elements[n]=e[n+t];return this}set(e,t,n,s){let r=this.elements;return r[0]=e,r[2]=t,r[1]=n,r[3]=s,this}};var $a=class extends _i{constructor(e,t=null){super(),this.object=e,this.domElement=t,this.enabled=!0,this.state=-1,this.keys={},this.mouseButtons={LEFT:null,MIDDLE:null,RIGHT:null},this.touches={ONE:null,TWO:null}}connect(e){this.domElement!==null&&this.disconnect(),this.domElement=e}disconnect(){}dispose(){}update(){}};function xu(i,e,t,n){let s=Tg(n);switch(t){case cu:return i*e;case Vc:return i*e/s.components*s.byteLength;case Wc:return i*e/s.components*s.byteLength;case Ps:return i*e*2/s.components*s.byteLength;case qc:return i*e*2/s.components*s.byteLength;case lu:return i*e*3/s.components*s.byteLength;case jn:return i*e*4/s.components*s.byteLength;case Xc:return i*e*4/s.components*s.byteLength;case no:case io:return Math.floor((i+3)/4)*Math.floor((e+3)/4)*8;case so:case ro:return Math.floor((i+3)/4)*Math.floor((e+3)/4)*16;case Kc:case Jc:return Math.max(i,16)*Math.max(e,8)/4;case jc:case Yc:return Math.max(i,8)*Math.max(e,8)/2;case Zc:case $c:case el:case tl:return Math.floor((i+3)/4)*Math.floor((e+3)/4)*8;case Qc:case ao:case nl:return Math.floor((i+3)/4)*Math.floor((e+3)/4)*16;case il:return Math.floor((i+3)/4)*Math.floor((e+3)/4)*16;case sl:return Math.floor((i+4)/5)*Math.floor((e+3)/4)*16;case rl:return Math.floor((i+4)/5)*Math.floor((e+4)/5)*16;case al:return Math.floor((i+5)/6)*Math.floor((e+4)/5)*16;case ol:return Math.floor((i+5)/6)*Math.floor((e+5)/6)*16;case cl:return Math.floor((i+7)/8)*Math.floor((e+4)/5)*16;case ll:return Math.floor((i+7)/8)*Math.floor((e+5)/6)*16;case hl:return Math.floor((i+7)/8)*Math.floor((e+7)/8)*16;case ul:return Math.floor((i+9)/10)*Math.floor((e+4)/5)*16;case dl:return Math.floor((i+9)/10)*Math.floor((e+5)/6)*16;case fl:return Math.floor((i+9)/10)*Math.floor((e+7)/8)*16;case pl:return Math.floor((i+9)/10)*Math.floor((e+9)/10)*16;case ml:return Math.floor((i+11)/12)*Math.floor((e+9)/10)*16;case gl:return Math.floor((i+11)/12)*Math.floor((e+11)/12)*16;case bl:case xl:case _l:return Math.ceil(i/4)*Math.ceil(e/4)*16;case yl:case vl:return Math.ceil(i/4)*Math.ceil(e/4)*8;case oo:case Ml:return Math.ceil(i/4)*Math.ceil(e/4)*16}throw new Error(`Unable to determine texture byte length for ${t} format.`)}function Tg(i){switch(i){case Un:case su:return{byteLength:1,components:1};case Yr:case ru:case Si:return{byteLength:2,components:1};case Hc:case Gc:return{byteLength:2,components:4};case Mi:case zc:case Xn:return{byteLength:4,components:1};case au:case ou:return{byteLength:4,components:3}}throw new Error(`THREE.TextureUtils: Unknown texture type ${i}.`)}typeof __THREE_DEVTOOLS__<"u"&&__THREE_DEVTOOLS__.dispatchEvent(new CustomEvent("register",{detail:{revision:"186"}}));typeof window<"u"&&(window.__THREE__?Ve("WARNING: Multiple instances of Three.js being imported."):window.__THREE__="186");function jp(){let i=null,e=!1,t=null,n=null;function s(r,a){n=i.requestAnimationFrame(s),t(r,a)}return{start:function(){e!==!0&&t!==null&&i!==null&&(n=i.requestAnimationFrame(s),e=!0)},stop:function(){i!==null&&i.cancelAnimationFrame(n),e=!1},setAnimationLoop:function(r){t=r},setContext:function(r){i=r}}}function Ag(i){let e=new WeakMap;function t(o,c){let l=o.array,h=o.usage,u=l.byteLength,d=i.createBuffer();i.bindBuffer(c,d),i.bufferData(c,l,h),o.onUploadCallback();let f;if(l instanceof Float32Array)f=i.FLOAT;else if(typeof Float16Array<"u"&&l instanceof Float16Array)f=i.HALF_FLOAT;else if(l instanceof Uint16Array)o.isFloat16BufferAttribute?f=i.HALF_FLOAT:f=i.UNSIGNED_SHORT;else if(l instanceof Int16Array)f=i.SHORT;else if(l instanceof Uint32Array)f=i.UNSIGNED_INT;else if(l instanceof Int32Array)f=i.INT;else if(l instanceof Int8Array)f=i.BYTE;else if(l instanceof Uint8Array)f=i.UNSIGNED_BYTE;else if(l instanceof Uint8ClampedArray)f=i.UNSIGNED_BYTE;else throw new Error("THREE.WebGLAttributes: Unsupported buffer data format: "+l);return{buffer:d,type:f,bytesPerElement:l.BYTES_PER_ELEMENT,version:o.version,size:u}}function n(o,c,l){let h=c.array,u=c.updateRanges;if(i.bindBuffer(l,o),u.length===0)i.bufferSubData(l,0,h);else{u.sort((f,m)=>f.start-m.start);let d=0;for(let f=1;f<u.length;f++){let m=u[d],b=u[f];b.start<=m.start+m.count+1?m.count=Math.max(m.count,b.start+b.count-m.start):(++d,u[d]=b)}u.length=d+1;for(let f=0,m=u.length;f<m;f++){let b=u[f];i.bufferSubData(l,b.start*h.BYTES_PER_ELEMENT,h,b.start,b.count)}c.clearUpdateRanges()}c.onUploadCallback()}function s(o){return o.isInterleavedBufferAttribute&&(o=o.data),e.get(o)}function r(o){o.isInterleavedBufferAttribute&&(o=o.data);let c=e.get(o);c&&(i.deleteBuffer(c.buffer),e.delete(o))}function a(o,c){if(o.isInterleavedBufferAttribute&&(o=o.data),o.isGLBufferAttribute){let h=e.get(o);(!h||h.version<o.version)&&e.set(o,{buffer:o.buffer,type:o.type,bytesPerElement:o.elementSize,version:o.version});return}let l=e.get(o);if(l===void 0)e.set(o,t(o,c));else if(l.version<o.version){if(l.size!==o.array.byteLength)throw new Error("THREE.WebGLAttributes: The size of the buffer attribute's array buffer does not match the original size. Resizing buffer attributes is not supported.");n(l.buffer,o,c),l.version=o.version}}return{get:s,remove:r,update:a}}var Rg=`#ifdef USE_ALPHAHASH
	if ( diffuseColor.a < getAlphaHashThreshold( vPosition ) ) discard;
#endif`,Cg=`#ifdef USE_ALPHAHASH
	const float ALPHA_HASH_SCALE = 0.05;
	float hash2D( vec2 value ) {
		return fract( 1.0e4 * sin( 17.0 * value.x + 0.1 * value.y ) * ( 0.1 + abs( sin( 13.0 * value.y + value.x ) ) ) );
	}
	float hash3D( vec3 value ) {
		return hash2D( vec2( hash2D( value.xy ), value.z ) );
	}
	float getAlphaHashThreshold( vec3 position ) {
		float maxDeriv = max(
			length( dFdx( position.xyz ) ),
			length( dFdy( position.xyz ) )
		);
		float pixScale = 1.0 / ( ALPHA_HASH_SCALE * maxDeriv );
		vec2 pixScales = vec2(
			exp2( floor( log2( pixScale ) ) ),
			exp2( ceil( log2( pixScale ) ) )
		);
		vec2 alpha = vec2(
			hash3D( floor( pixScales.x * position.xyz ) ),
			hash3D( floor( pixScales.y * position.xyz ) )
		);
		float lerpFactor = fract( log2( pixScale ) );
		float x = ( 1.0 - lerpFactor ) * alpha.x + lerpFactor * alpha.y;
		float a = min( lerpFactor, 1.0 - lerpFactor );
		vec3 cases = vec3(
			x * x / ( 2.0 * a * ( 1.0 - a ) ),
			( x - 0.5 * a ) / ( 1.0 - a ),
			1.0 - ( ( 1.0 - x ) * ( 1.0 - x ) / ( 2.0 * a * ( 1.0 - a ) ) )
		);
		float threshold = ( x < ( 1.0 - a ) )
			? ( ( x < a ) ? cases.x : cases.y )
			: cases.z;
		return clamp( threshold , 1.0e-6, 1.0 );
	}
#endif`,Pg=`#ifdef USE_ALPHAMAP
	diffuseColor.a *= texture2D( alphaMap, vAlphaMapUv ).g;
#endif`,Ig=`#ifdef USE_ALPHAMAP
	uniform sampler2D alphaMap;
#endif`,Lg=`#ifdef USE_ALPHATEST
	#ifdef ALPHA_TO_COVERAGE
	diffuseColor.a = smoothstep( alphaTest, alphaTest + fwidth( diffuseColor.a ), diffuseColor.a );
	if ( diffuseColor.a == 0.0 ) discard;
	#else
	if ( diffuseColor.a < alphaTest ) discard;
	#endif
#endif`,Dg=`#ifdef USE_ALPHATEST
	uniform float alphaTest;
#endif`,Ng=`#ifdef USE_AOMAP
	float ambientOcclusion = ( texture2D( aoMap, vAoMapUv ).r - 1.0 ) * aoMapIntensity + 1.0;
	reflectedLight.indirectDiffuse *= ambientOcclusion;
	#if defined( USE_CLEARCOAT ) 
		clearcoatSpecularIndirect *= ambientOcclusion;
	#endif
	#if defined( USE_SHEEN ) 
		sheenSpecularIndirect *= ambientOcclusion;
	#endif
	#if defined( USE_ENVMAP ) && defined( STANDARD )
		float dotNV = saturate( dot( geometryNormal, geometryViewDir ) );
		reflectedLight.indirectSpecular *= computeSpecularOcclusion( dotNV, ambientOcclusion, material.roughness );
	#endif
#endif`,Fg=`#ifdef USE_AOMAP
	uniform sampler2D aoMap;
	uniform float aoMapIntensity;
#endif`,Ug=`#ifdef USE_BATCHING
	#if ! defined( GL_ANGLE_multi_draw )
	#define gl_DrawID _gl_DrawID
	uniform int _gl_DrawID;
	#endif
	uniform highp sampler2D batchingTexture;
	uniform highp usampler2D batchingIdTexture;
	mat4 getBatchingMatrix( const in float i ) {
		int size = textureSize( batchingTexture, 0 ).x;
		int j = int( i ) * 4;
		int x = j % size;
		int y = j / size;
		vec4 v1 = texelFetch( batchingTexture, ivec2( x, y ), 0 );
		vec4 v2 = texelFetch( batchingTexture, ivec2( x + 1, y ), 0 );
		vec4 v3 = texelFetch( batchingTexture, ivec2( x + 2, y ), 0 );
		vec4 v4 = texelFetch( batchingTexture, ivec2( x + 3, y ), 0 );
		return mat4( v1, v2, v3, v4 );
	}
	float getIndirectIndex( const in int i ) {
		int size = textureSize( batchingIdTexture, 0 ).x;
		int x = i % size;
		int y = i / size;
		return float( texelFetch( batchingIdTexture, ivec2( x, y ), 0 ).r );
	}
#endif
#ifdef USE_BATCHING_COLOR
	uniform sampler2D batchingColorTexture;
	vec4 getBatchingColor( const in float i ) {
		int size = textureSize( batchingColorTexture, 0 ).x;
		int j = int( i );
		int x = j % size;
		int y = j / size;
		return texelFetch( batchingColorTexture, ivec2( x, y ), 0 );
	}
#endif`,Og=`#ifdef USE_BATCHING
	mat4 batchingMatrix = getBatchingMatrix( getIndirectIndex( gl_DrawID ) );
#endif`,Bg=`vec3 transformed = vec3( position );
#ifdef USE_ALPHAHASH
	vPosition = vec3( position );
#endif`,kg=`vec3 objectNormal = vec3( normal );
#ifdef USE_TANGENT
	vec3 objectTangent = vec3( tangent.xyz );
#endif`,zg=`float G_BlinnPhong_Implicit( ) {
	return 0.25;
}
float D_BlinnPhong( const in float shininess, const in float dotNH ) {
	return RECIPROCAL_PI * ( shininess * 0.5 + 1.0 ) * pow( dotNH, shininess );
}
vec3 BRDF_BlinnPhong( const in vec3 lightDir, const in vec3 viewDir, const in vec3 normal, const in vec3 specularColor, const in float shininess ) {
	vec3 halfDir = normalize( lightDir + viewDir );
	float dotNH = saturate( dot( normal, halfDir ) );
	float dotVH = saturate( dot( viewDir, halfDir ) );
	vec3 F = F_Schlick( specularColor, 1.0, dotVH );
	float G = G_BlinnPhong_Implicit( );
	float D = D_BlinnPhong( shininess, dotNH );
	return F * ( G * D );
} // validated`,Hg=`#ifdef USE_IRIDESCENCE
	const mat3 XYZ_TO_REC709 = mat3(
		 3.2404542, -0.9692660,  0.0556434,
		-1.5371385,  1.8760108, -0.2040259,
		-0.4985314,  0.0415560,  1.0572252
	);
	vec3 Fresnel0ToIor( vec3 fresnel0 ) {
		vec3 sqrtF0 = sqrt( fresnel0 );
		return ( vec3( 1.0 ) + sqrtF0 ) / ( vec3( 1.0 ) - sqrtF0 );
	}
	vec3 IorToFresnel0( vec3 transmittedIor, float incidentIor ) {
		return pow2( ( transmittedIor - vec3( incidentIor ) ) / ( transmittedIor + vec3( incidentIor ) ) );
	}
	float IorToFresnel0( float transmittedIor, float incidentIor ) {
		return pow2( ( transmittedIor - incidentIor ) / ( transmittedIor + incidentIor ));
	}
	vec3 evalSensitivity( float OPD, vec3 shift ) {
		float phase = 2.0 * PI * OPD * 1.0e-9;
		vec3 val = vec3( 5.4856e-13, 4.4201e-13, 5.2481e-13 );
		vec3 pos = vec3( 1.6810e+06, 1.7953e+06, 2.2084e+06 );
		vec3 var = vec3( 4.3278e+09, 9.3046e+09, 6.6121e+09 );
		vec3 xyz = val * sqrt( 2.0 * PI * var ) * cos( pos * phase + shift ) * exp( - pow2( phase ) * var );
		xyz.x += 9.7470e-14 * sqrt( 2.0 * PI * 4.5282e+09 ) * cos( 2.2399e+06 * phase + shift[ 0 ] ) * exp( - 4.5282e+09 * pow2( phase ) );
		xyz /= 1.0685e-7;
		vec3 rgb = XYZ_TO_REC709 * xyz;
		return rgb;
	}
	vec3 evalIridescence( float outsideIOR, float eta2, float cosTheta1, float thinFilmThickness, vec3 baseF0 ) {
		vec3 I;
		float iridescenceIOR = mix( outsideIOR, eta2, smoothstep( 0.0, 0.03, thinFilmThickness ) );
		float sinTheta2Sq = pow2( outsideIOR / iridescenceIOR ) * ( 1.0 - pow2( cosTheta1 ) );
		float cosTheta2Sq = 1.0 - sinTheta2Sq;
		if ( cosTheta2Sq < 0.0 ) {
			return vec3( 1.0 );
		}
		float cosTheta2 = sqrt( cosTheta2Sq );
		float R0 = IorToFresnel0( iridescenceIOR, outsideIOR );
		float R12 = F_Schlick( R0, 1.0, cosTheta1 );
		float T121 = 1.0 - R12;
		float phi12 = 0.0;
		if ( iridescenceIOR < outsideIOR ) phi12 = PI;
		float phi21 = PI - phi12;
		vec3 baseIOR = Fresnel0ToIor( clamp( baseF0, 0.0, 0.9999 ) );		vec3 R1 = IorToFresnel0( baseIOR, iridescenceIOR );
		vec3 R23 = F_Schlick( R1, 1.0, cosTheta2 );
		vec3 phi23 = vec3( 0.0 );
		if ( baseIOR[ 0 ] < iridescenceIOR ) phi23[ 0 ] = PI;
		if ( baseIOR[ 1 ] < iridescenceIOR ) phi23[ 1 ] = PI;
		if ( baseIOR[ 2 ] < iridescenceIOR ) phi23[ 2 ] = PI;
		float OPD = 2.0 * iridescenceIOR * thinFilmThickness * cosTheta2;
		vec3 phi = vec3( phi21 ) + phi23;
		vec3 R123 = clamp( R12 * R23, 1e-5, 0.9999 );
		vec3 r123 = sqrt( R123 );
		vec3 Rs = pow2( T121 ) * R23 / ( vec3( 1.0 ) - R123 );
		vec3 C0 = R12 + Rs;
		I = C0;
		vec3 Cm = Rs - T121;
		for ( int m = 1; m <= 2; ++ m ) {
			Cm *= r123;
			vec3 Sm = 2.0 * evalSensitivity( float( m ) * OPD, float( m ) * phi );
			I += Cm * Sm;
		}
		return max( I, vec3( 0.0 ) );
	}
#endif`,Gg=`#ifdef USE_BUMPMAP
	uniform sampler2D bumpMap;
	uniform float bumpScale;
	vec2 dHdxy_fwd() {
		vec2 dSTdx = dFdx( vBumpMapUv );
		vec2 dSTdy = dFdy( vBumpMapUv );
		float Hll = bumpScale * texture2D( bumpMap, vBumpMapUv ).x;
		float dBx = bumpScale * texture2D( bumpMap, vBumpMapUv + dSTdx ).x - Hll;
		float dBy = bumpScale * texture2D( bumpMap, vBumpMapUv + dSTdy ).x - Hll;
		return vec2( dBx, dBy );
	}
	vec3 perturbNormalArb( vec3 surf_pos, vec3 surf_norm, vec2 dHdxy, float faceDirection ) {
		vec3 vSigmaX = normalize( dFdx( surf_pos.xyz ) );
		vec3 vSigmaY = normalize( dFdy( surf_pos.xyz ) );
		vec3 vN = surf_norm;
		vec3 R1 = cross( vSigmaY, vN );
		vec3 R2 = cross( vN, vSigmaX );
		float fDet = dot( vSigmaX, R1 ) * faceDirection;
		vec3 vGrad = sign( fDet ) * ( dHdxy.x * R1 + dHdxy.y * R2 );
		return normalize( abs( fDet ) * surf_norm - vGrad );
	}
#endif`,Vg=`#if NUM_CLIPPING_PLANES > 0
	vec4 plane;
	#ifdef ALPHA_TO_COVERAGE
		float distanceToPlane, distanceGradient;
		float clipOpacity = 1.0;
		#pragma unroll_loop_start
		for ( int i = 0; i < UNION_CLIPPING_PLANES; i ++ ) {
			plane = clippingPlanes[ i ];
			distanceToPlane = - dot( vClipPosition, plane.xyz ) + plane.w;
			distanceGradient = fwidth( distanceToPlane ) / 2.0;
			clipOpacity *= smoothstep( - distanceGradient, distanceGradient, distanceToPlane );
			if ( clipOpacity == 0.0 ) discard;
		}
		#pragma unroll_loop_end
		#if UNION_CLIPPING_PLANES < NUM_CLIPPING_PLANES
			float unionClipOpacity = 1.0;
			#pragma unroll_loop_start
			for ( int i = UNION_CLIPPING_PLANES; i < NUM_CLIPPING_PLANES; i ++ ) {
				plane = clippingPlanes[ i ];
				distanceToPlane = - dot( vClipPosition, plane.xyz ) + plane.w;
				distanceGradient = fwidth( distanceToPlane ) / 2.0;
				unionClipOpacity *= 1.0 - smoothstep( - distanceGradient, distanceGradient, distanceToPlane );
			}
			#pragma unroll_loop_end
			clipOpacity *= 1.0 - unionClipOpacity;
		#endif
		diffuseColor.a *= clipOpacity;
		if ( diffuseColor.a == 0.0 ) discard;
	#else
		#pragma unroll_loop_start
		for ( int i = 0; i < UNION_CLIPPING_PLANES; i ++ ) {
			plane = clippingPlanes[ i ];
			if ( dot( vClipPosition, plane.xyz ) > plane.w ) discard;
		}
		#pragma unroll_loop_end
		#if UNION_CLIPPING_PLANES < NUM_CLIPPING_PLANES
			bool clipped = true;
			#pragma unroll_loop_start
			for ( int i = UNION_CLIPPING_PLANES; i < NUM_CLIPPING_PLANES; i ++ ) {
				plane = clippingPlanes[ i ];
				clipped = ( dot( vClipPosition, plane.xyz ) > plane.w ) && clipped;
			}
			#pragma unroll_loop_end
			if ( clipped ) discard;
		#endif
	#endif
#endif`,Wg=`#if NUM_CLIPPING_PLANES > 0
	varying vec3 vClipPosition;
	uniform vec4 clippingPlanes[ NUM_CLIPPING_PLANES ];
#endif`,qg=`#if NUM_CLIPPING_PLANES > 0
	varying vec3 vClipPosition;
#endif`,Xg=`#if NUM_CLIPPING_PLANES > 0
	vClipPosition = - mvPosition.xyz;
#endif`,jg=`#if defined( USE_COLOR ) || defined( USE_COLOR_ALPHA )
	diffuseColor *= vColor;
#endif`,Kg=`#if defined( USE_COLOR ) || defined( USE_COLOR_ALPHA )
	varying vec4 vColor;
#endif`,Yg=`#if defined( USE_COLOR ) || defined( USE_COLOR_ALPHA ) || defined( USE_INSTANCING_COLOR ) || defined( USE_BATCHING_COLOR )
	varying vec4 vColor;
#endif`,Jg=`#if defined( USE_COLOR ) || defined( USE_COLOR_ALPHA ) || defined( USE_INSTANCING_COLOR ) || defined( USE_BATCHING_COLOR )
	vColor = vec4( 1.0 );
#endif
#ifdef USE_COLOR_ALPHA
	vColor *= color;
#elif defined( USE_COLOR )
	vColor.rgb *= color;
#endif
#ifdef USE_INSTANCING_COLOR
	vColor.rgb *= instanceColor.rgb;
#endif
#ifdef USE_BATCHING_COLOR
	vColor *= getBatchingColor( getIndirectIndex( gl_DrawID ) );
#endif`,Zg=`#define PI 3.141592653589793
#define PI2 6.283185307179586
#define PI_HALF 1.5707963267948966
#define RECIPROCAL_PI 0.3183098861837907
#define RECIPROCAL_PI2 0.15915494309189535
#define EPSILON 1e-6
#ifndef saturate
#define saturate( a ) clamp( a, 0.0, 1.0 )
#endif
#define whiteComplement( a ) ( 1.0 - saturate( a ) )
float pow2( const in float x ) { return x*x; }
vec3 pow2( const in vec3 x ) { return x*x; }
float pow3( const in float x ) { return x*x*x; }
float pow4( const in float x ) { float x2 = x*x; return x2*x2; }
float max3( const in vec3 v ) { return max( max( v.x, v.y ), v.z ); }
float average( const in vec3 v ) { return dot( v, vec3( 0.3333333 ) ); }
highp float rand( const in vec2 uv ) {
	const highp float a = 12.9898, b = 78.233, c = 43758.5453;
	highp float dt = dot( uv.xy, vec2( a,b ) ), sn = mod( dt, PI );
	return fract( sin( sn ) * c );
}
#ifdef HIGH_PRECISION
	float precisionSafeLength( vec3 v ) { return length( v ); }
#else
	float precisionSafeLength( vec3 v ) {
		float maxComponent = max3( abs( v ) );
		return length( v / maxComponent ) * maxComponent;
	}
#endif
struct IncidentLight {
	vec3 color;
	vec3 direction;
	bool visible;
};
struct ReflectedLight {
	vec3 directDiffuse;
	vec3 directSpecular;
	vec3 indirectDiffuse;
	vec3 indirectSpecular;
};
#ifdef USE_ALPHAHASH
	varying vec3 vPosition;
#endif
vec3 transformDirection( in vec3 dir, in mat4 matrix ) {
	return normalize( ( matrix * vec4( dir, 0.0 ) ).xyz );
}
#define inverseTransformDirection transformDirectionByInverseViewMatrix
vec3 transformNormalByInverseViewMatrix( in vec3 normal, in mat4 viewMatrix ) {
	return normalize( ( vec4( normal, 0.0 ) * viewMatrix ).xyz );
}
vec3 transformDirectionByInverseViewMatrix( in vec3 dir, in mat4 viewMatrix ) {
	return normalize( ( vec4( dir, 0.0 ) * viewMatrix ).xyz );
}
bool isPerspectiveMatrix( mat4 m ) {
	return m[ 2 ][ 3 ] == - 1.0;
}
vec2 equirectUv( in vec3 dir ) {
	float u = atan( dir.z, dir.x ) * RECIPROCAL_PI2 + 0.5;
	float v = asin( clamp( dir.y, - 1.0, 1.0 ) ) * RECIPROCAL_PI + 0.5;
	return vec2( u, v );
}
vec3 BRDF_Lambert( const in vec3 diffuseColor ) {
	return RECIPROCAL_PI * diffuseColor;
}
vec3 F_Schlick( const in vec3 f0, const in float f90, const in float dotVH ) {
	float fresnel = exp2( ( - 5.55473 * dotVH - 6.98316 ) * dotVH );
	return f0 * ( 1.0 - fresnel ) + ( f90 * fresnel );
}
float F_Schlick( const in float f0, const in float f90, const in float dotVH ) {
	float fresnel = exp2( ( - 5.55473 * dotVH - 6.98316 ) * dotVH );
	return f0 * ( 1.0 - fresnel ) + ( f90 * fresnel );
} // validated`,$g=`#ifdef ENVMAP_TYPE_CUBE_UV
	#define cubeUV_minMipLevel 4.0
	#define cubeUV_minTileSize 16.0
	float getFace( vec3 direction ) {
		vec3 absDirection = abs( direction );
		float face = - 1.0;
		if ( absDirection.x > absDirection.z ) {
			if ( absDirection.x > absDirection.y )
				face = direction.x > 0.0 ? 0.0 : 3.0;
			else
				face = direction.y > 0.0 ? 1.0 : 4.0;
		} else {
			if ( absDirection.z > absDirection.y )
				face = direction.z > 0.0 ? 2.0 : 5.0;
			else
				face = direction.y > 0.0 ? 1.0 : 4.0;
		}
		return face;
	}
	vec2 getUV( vec3 direction, float face ) {
		vec2 uv;
		if ( face == 0.0 ) {
			uv = vec2( direction.z, direction.y ) / abs( direction.x );
		} else if ( face == 1.0 ) {
			uv = vec2( - direction.x, - direction.z ) / abs( direction.y );
		} else if ( face == 2.0 ) {
			uv = vec2( - direction.x, direction.y ) / abs( direction.z );
		} else if ( face == 3.0 ) {
			uv = vec2( - direction.z, direction.y ) / abs( direction.x );
		} else if ( face == 4.0 ) {
			uv = vec2( - direction.x, direction.z ) / abs( direction.y );
		} else {
			uv = vec2( direction.x, direction.y ) / abs( direction.z );
		}
		return 0.5 * ( uv + 1.0 );
	}
	vec3 bilinearCubeUV( sampler2D envMap, vec3 direction, float mipInt ) {
		float face = getFace( direction );
		float filterInt = max( cubeUV_minMipLevel - mipInt, 0.0 );
		mipInt = max( mipInt, cubeUV_minMipLevel );
		float faceSize = exp2( mipInt );
		highp vec2 uv = getUV( direction, face ) * ( faceSize - 2.0 ) + 1.0;
		if ( face > 2.0 ) {
			uv.y += faceSize;
			face -= 3.0;
		}
		uv.x += face * faceSize;
		uv.x += filterInt * 3.0 * cubeUV_minTileSize;
		uv.y += 4.0 * ( exp2( CUBEUV_MAX_MIP ) - faceSize );
		uv.x *= CUBEUV_TEXEL_WIDTH;
		uv.y *= CUBEUV_TEXEL_HEIGHT;
		#ifdef texture2DGradEXT
			return texture2DGradEXT( envMap, uv, vec2( 0.0 ), vec2( 0.0 ) ).rgb;
		#else
			return texture2D( envMap, uv ).rgb;
		#endif
	}
	#define cubeUV_r0 1.0
	#define cubeUV_m0 - 2.0
	#define cubeUV_r1 0.8
	#define cubeUV_m1 - 1.0
	#define cubeUV_r4 0.4
	#define cubeUV_m4 2.0
	#define cubeUV_r5 0.305
	#define cubeUV_m5 3.0
	#define cubeUV_r6 0.21
	#define cubeUV_m6 4.0
	float roughnessToMip( float roughness ) {
		float mip = 0.0;
		if ( roughness >= cubeUV_r1 ) {
			mip = ( cubeUV_r0 - roughness ) * ( cubeUV_m1 - cubeUV_m0 ) / ( cubeUV_r0 - cubeUV_r1 ) + cubeUV_m0;
		} else if ( roughness >= cubeUV_r4 ) {
			mip = ( cubeUV_r1 - roughness ) * ( cubeUV_m4 - cubeUV_m1 ) / ( cubeUV_r1 - cubeUV_r4 ) + cubeUV_m1;
		} else if ( roughness >= cubeUV_r5 ) {
			mip = ( cubeUV_r4 - roughness ) * ( cubeUV_m5 - cubeUV_m4 ) / ( cubeUV_r4 - cubeUV_r5 ) + cubeUV_m4;
		} else if ( roughness >= cubeUV_r6 ) {
			mip = ( cubeUV_r5 - roughness ) * ( cubeUV_m6 - cubeUV_m5 ) / ( cubeUV_r5 - cubeUV_r6 ) + cubeUV_m5;
		} else {
			mip = - 2.0 * log2( 1.16 * roughness );		}
		return mip;
	}
	vec4 textureCubeUV( sampler2D envMap, vec3 sampleDir, float roughness ) {
		float mip = clamp( roughnessToMip( roughness ), cubeUV_m0, CUBEUV_MAX_MIP );
		float mipF = fract( mip );
		float mipInt = floor( mip );
		vec3 color0 = bilinearCubeUV( envMap, sampleDir, mipInt );
		if ( mipF == 0.0 ) {
			return vec4( color0, 1.0 );
		} else {
			vec3 color1 = bilinearCubeUV( envMap, sampleDir, mipInt + 1.0 );
			return vec4( mix( color0, color1, mipF ), 1.0 );
		}
	}
#endif`,Qg=`vec3 transformedNormal = objectNormal;
#ifdef USE_TANGENT
	vec3 transformedTangent = objectTangent;
#endif
#ifdef USE_BATCHING
	mat3 bm = mat3( batchingMatrix );
	transformedNormal /= vec3( dot( bm[ 0 ], bm[ 0 ] ), dot( bm[ 1 ], bm[ 1 ] ), dot( bm[ 2 ], bm[ 2 ] ) );
	transformedNormal = bm * transformedNormal;
	#ifdef USE_TANGENT
		transformedTangent = bm * transformedTangent;
	#endif
#endif
#ifdef USE_INSTANCING
	mat3 im = mat3( instanceMatrix );
	transformedNormal /= vec3( dot( im[ 0 ], im[ 0 ] ), dot( im[ 1 ], im[ 1 ] ), dot( im[ 2 ], im[ 2 ] ) );
	transformedNormal = im * transformedNormal;
	#ifdef USE_TANGENT
		transformedTangent = im * transformedTangent;
	#endif
#endif
transformedNormal = normalMatrix * transformedNormal;
#ifdef FLIP_SIDED
	transformedNormal = - transformedNormal;
#endif
#ifdef USE_TANGENT
	transformedTangent = ( modelViewMatrix * vec4( transformedTangent, 0.0 ) ).xyz;
#endif`,eb=`#ifdef USE_DISPLACEMENTMAP
	uniform sampler2D displacementMap;
	uniform float displacementScale;
	uniform float displacementBias;
#endif`,tb=`#ifdef USE_DISPLACEMENTMAP
	transformed += normalize( objectNormal ) * ( texture2D( displacementMap, vDisplacementMapUv ).x * displacementScale + displacementBias );
#endif`,nb=`#ifdef USE_EMISSIVEMAP
	vec4 emissiveColor = texture2D( emissiveMap, vEmissiveMapUv );
	#ifdef DECODE_VIDEO_TEXTURE_EMISSIVE
		emissiveColor = sRGBTransferEOTF( emissiveColor );
	#endif
	totalEmissiveRadiance *= emissiveColor.rgb;
#endif`,ib=`#ifdef USE_EMISSIVEMAP
	uniform sampler2D emissiveMap;
#endif`,sb="gl_FragColor = linearToOutputTexel( gl_FragColor );",rb=`vec4 LinearTransferOETF( in vec4 value ) {
	return value;
}
vec4 sRGBTransferEOTF( in vec4 value ) {
	return vec4( mix( pow( value.rgb * 0.9478672986 + vec3( 0.0521327014 ), vec3( 2.4 ) ), value.rgb * 0.0773993808, vec3( lessThanEqual( value.rgb, vec3( 0.04045 ) ) ) ), value.a );
}
vec4 sRGBTransferOETF( in vec4 value ) {
	return vec4( mix( pow( value.rgb, vec3( 0.41666 ) ) * 1.055 - vec3( 0.055 ), value.rgb * 12.92, vec3( lessThanEqual( value.rgb, vec3( 0.0031308 ) ) ) ), value.a );
}`,ab=`#ifdef USE_ENVMAP
	#ifdef ENV_WORLDPOS
		vec3 cameraToFrag;
		if ( isOrthographic ) {
			cameraToFrag = normalize( vec3( - viewMatrix[ 0 ][ 2 ], - viewMatrix[ 1 ][ 2 ], - viewMatrix[ 2 ][ 2 ] ) );
		} else {
			cameraToFrag = normalize( vWorldPosition - cameraPosition );
		}
		vec3 worldNormal = transformNormalByInverseViewMatrix( normal, viewMatrix );
		#ifdef ENVMAP_MODE_REFLECTION
			vec3 reflectVec = reflect( cameraToFrag, worldNormal );
		#else
			vec3 reflectVec = refract( cameraToFrag, worldNormal, refractionRatio );
		#endif
	#else
		vec3 reflectVec = vReflect;
	#endif
	#ifdef ENVMAP_TYPE_CUBE
		vec4 envColor = textureCube( envMap, envMapRotation * reflectVec );
		#ifdef ENVMAP_BLENDING_MULTIPLY
			outgoingLight = mix( outgoingLight, outgoingLight * envColor.xyz, specularStrength * reflectivity );
		#elif defined( ENVMAP_BLENDING_MIX )
			outgoingLight = mix( outgoingLight, envColor.xyz, specularStrength * reflectivity );
		#elif defined( ENVMAP_BLENDING_ADD )
			outgoingLight += envColor.xyz * specularStrength * reflectivity;
		#endif
	#endif
#endif`,ob=`#ifdef USE_ENVMAP
	uniform float envMapIntensity;
	uniform mat3 envMapRotation;
	#ifdef ENVMAP_TYPE_CUBE
		uniform samplerCube envMap;
	#else
		uniform sampler2D envMap;
	#endif
#endif`,cb=`#ifdef USE_ENVMAP
	uniform float reflectivity;
	#if defined( USE_BUMPMAP ) || defined( USE_NORMALMAP ) || defined( PHONG ) || defined( LAMBERT )
		#define ENV_WORLDPOS
	#endif
	#ifdef ENV_WORLDPOS
		varying vec3 vWorldPosition;
		uniform float refractionRatio;
	#else
		varying vec3 vReflect;
	#endif
#endif`,lb=`#ifdef USE_ENVMAP
	#if defined( USE_BUMPMAP ) || defined( USE_NORMALMAP ) || defined( PHONG ) || defined( LAMBERT )
		#define ENV_WORLDPOS
	#endif
	#ifdef ENV_WORLDPOS
		
		varying vec3 vWorldPosition;
	#else
		varying vec3 vReflect;
		uniform float refractionRatio;
	#endif
#endif`,hb=`#ifdef USE_ENVMAP
	#ifdef ENV_WORLDPOS
		vWorldPosition = worldPosition.xyz;
	#else
		vec3 cameraToVertex;
		if ( isOrthographic ) {
			cameraToVertex = normalize( vec3( - viewMatrix[ 0 ][ 2 ], - viewMatrix[ 1 ][ 2 ], - viewMatrix[ 2 ][ 2 ] ) );
		} else {
			cameraToVertex = normalize( worldPosition.xyz - cameraPosition );
		}
		vec3 worldNormal = transformNormalByInverseViewMatrix( transformedNormal, viewMatrix );
		#ifdef ENVMAP_MODE_REFLECTION
			vReflect = reflect( cameraToVertex, worldNormal );
		#else
			vReflect = refract( cameraToVertex, worldNormal, refractionRatio );
		#endif
	#endif
#endif`,ub=`#ifdef USE_FOG
	vFogDepth = - mvPosition.z;
#endif`,db=`#ifdef USE_FOG
	varying float vFogDepth;
#endif`,fb=`#ifdef USE_FOG
	#ifdef FOG_EXP2
		float fogFactor = 1.0 - exp( - fogDensity * fogDensity * vFogDepth * vFogDepth );
	#else
		float fogFactor = smoothstep( fogNear, fogFar, vFogDepth );
	#endif
	gl_FragColor.rgb = mix( gl_FragColor.rgb, fogColor, fogFactor );
#endif`,pb=`#ifdef USE_FOG
	uniform vec3 fogColor;
	varying float vFogDepth;
	#ifdef FOG_EXP2
		uniform float fogDensity;
	#else
		uniform float fogNear;
		uniform float fogFar;
	#endif
#endif`,mb=`#ifdef USE_GRADIENTMAP
	uniform sampler2D gradientMap;
#endif
vec3 getGradientIrradiance( vec3 normal, vec3 lightDirection ) {
	float dotNL = dot( normal, lightDirection );
	vec2 coord = vec2( dotNL * 0.5 + 0.5, 0.0 );
	#ifdef USE_GRADIENTMAP
		return vec3( texture2D( gradientMap, coord ).r );
	#else
		vec2 fw = fwidth( coord ) * 0.5;
		return mix( vec3( 0.7 ), vec3( 1.0 ), smoothstep( 0.7 - fw.x, 0.7 + fw.x, coord.x ) );
	#endif
}`,gb=`#ifdef USE_LIGHTMAP
	uniform sampler2D lightMap;
	uniform float lightMapIntensity;
#endif`,bb=`LambertMaterial material;
material.diffuseColor = diffuseColor.rgb;
material.specularStrength = specularStrength;`,xb=`varying vec3 vViewPosition;
struct LambertMaterial {
	vec3 diffuseColor;
	float specularStrength;
};
void RE_Direct_Lambert( const in IncidentLight directLight, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in LambertMaterial material, inout ReflectedLight reflectedLight ) {
	float dotNL = saturate( dot( geometryNormal, directLight.direction ) );
	vec3 irradiance = dotNL * directLight.color;
	reflectedLight.directDiffuse += irradiance * BRDF_Lambert( material.diffuseColor );
}
void RE_IndirectDiffuse_Lambert( const in vec3 irradiance, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in LambertMaterial material, inout ReflectedLight reflectedLight ) {
	reflectedLight.indirectDiffuse += irradiance * BRDF_Lambert( material.diffuseColor );
}
#define RE_Direct				RE_Direct_Lambert
#define RE_IndirectDiffuse		RE_IndirectDiffuse_Lambert`,_b=`uniform bool receiveShadow;
uniform vec3 ambientLightColor;
#if defined( USE_LIGHT_PROBES )
	uniform vec3 lightProbe[ 9 ];
#endif
vec3 shGetIrradianceAt( in vec3 normal, in vec3 shCoefficients[ 9 ] ) {
	float x = normal.x, y = normal.y, z = normal.z;
	vec3 result = shCoefficients[ 0 ] * 0.886227;
	result += shCoefficients[ 1 ] * 2.0 * 0.511664 * y;
	result += shCoefficients[ 2 ] * 2.0 * 0.511664 * z;
	result += shCoefficients[ 3 ] * 2.0 * 0.511664 * x;
	result += shCoefficients[ 4 ] * 2.0 * 0.429043 * x * y;
	result += shCoefficients[ 5 ] * 2.0 * 0.429043 * y * z;
	result += shCoefficients[ 6 ] * ( 0.743125 * z * z - 0.247708 );
	result += shCoefficients[ 7 ] * 2.0 * 0.429043 * x * z;
	result += shCoefficients[ 8 ] * 0.429043 * ( x * x - y * y );
	return result;
}
vec3 getLightProbeIrradiance( const in vec3 lightProbe[ 9 ], const in vec3 normal ) {
	vec3 worldNormal = transformNormalByInverseViewMatrix( normal, viewMatrix );
	vec3 irradiance = shGetIrradianceAt( worldNormal, lightProbe );
	return irradiance;
}
vec3 getAmbientLightIrradiance( const in vec3 ambientLightColor ) {
	vec3 irradiance = ambientLightColor;
	return irradiance;
}
float getDistanceAttenuation( const in float lightDistance, const in float cutoffDistance, const in float decayExponent ) {
	float distanceFalloff = 1.0 / max( pow( lightDistance, decayExponent ), 0.01 );
	if ( cutoffDistance > 0.0 ) {
		distanceFalloff *= pow2( saturate( 1.0 - pow4( lightDistance / cutoffDistance ) ) );
	}
	return distanceFalloff;
}
float getSpotAttenuation( const in float coneCosine, const in float penumbraCosine, const in float angleCosine ) {
	return smoothstep( coneCosine, penumbraCosine, angleCosine );
}
#if NUM_SUN_LIGHTS > 0
	struct SunLight {
		vec3 direction;
		vec3 color;
	};
	uniform SunLight sunLights[ NUM_SUN_LIGHTS ];
	void getSunLightInfo( const in SunLight sunLight, out IncidentLight light ) {
		light.color = sunLight.color;
		light.direction = sunLight.direction;
		light.visible = true;
	}
#endif
#if NUM_DIR_LIGHTS > 0
	struct DirectionalLight {
		vec3 direction;
		vec3 color;
	};
	uniform DirectionalLight directionalLights[ NUM_DIR_LIGHTS ];
	void getDirectionalLightInfo( const in DirectionalLight directionalLight, out IncidentLight light ) {
		light.color = directionalLight.color;
		light.direction = directionalLight.direction;
		light.visible = true;
	}
#endif
#if NUM_POINT_LIGHTS > 0
	struct PointLight {
		vec3 position;
		vec3 color;
		float distance;
		float decay;
	};
	uniform PointLight pointLights[ NUM_POINT_LIGHTS ];
	void getPointLightInfo( const in PointLight pointLight, const in vec3 geometryPosition, out IncidentLight light ) {
		vec3 lVector = pointLight.position - geometryPosition;
		light.direction = normalize( lVector );
		float lightDistance = length( lVector );
		light.color = pointLight.color;
		light.color *= getDistanceAttenuation( lightDistance, pointLight.distance, pointLight.decay );
		light.visible = ( light.color != vec3( 0.0 ) );
	}
#endif
#if NUM_SPOT_LIGHTS > 0
	struct SpotLight {
		vec3 position;
		vec3 direction;
		vec3 color;
		float distance;
		float decay;
		float coneCos;
		float penumbraCos;
	};
	uniform SpotLight spotLights[ NUM_SPOT_LIGHTS ];
	void getSpotLightInfo( const in SpotLight spotLight, const in vec3 geometryPosition, out IncidentLight light ) {
		vec3 lVector = spotLight.position - geometryPosition;
		light.direction = normalize( lVector );
		float angleCos = dot( light.direction, spotLight.direction );
		float spotAttenuation = getSpotAttenuation( spotLight.coneCos, spotLight.penumbraCos, angleCos );
		if ( spotAttenuation > 0.0 ) {
			float lightDistance = length( lVector );
			light.color = spotLight.color * spotAttenuation;
			light.color *= getDistanceAttenuation( lightDistance, spotLight.distance, spotLight.decay );
			light.visible = ( light.color != vec3( 0.0 ) );
		} else {
			light.color = vec3( 0.0 );
			light.visible = false;
		}
	}
#endif
#if NUM_RECT_AREA_LIGHTS > 0
	struct RectAreaLight {
		vec3 color;
		vec3 position;
		vec3 halfWidth;
		vec3 halfHeight;
	};
	uniform sampler2D ltc_1;	uniform sampler2D ltc_2;
	uniform RectAreaLight rectAreaLights[ NUM_RECT_AREA_LIGHTS ];
#endif
#if NUM_HEMI_LIGHTS > 0
	struct HemisphereLight {
		vec3 direction;
		vec3 skyColor;
		vec3 groundColor;
	};
	uniform HemisphereLight hemisphereLights[ NUM_HEMI_LIGHTS ];
	vec3 getHemisphereLightIrradiance( const in HemisphereLight hemiLight, const in vec3 normal ) {
		float dotNL = dot( normal, hemiLight.direction );
		float hemiDiffuseWeight = 0.5 * dotNL + 0.5;
		vec3 irradiance = mix( hemiLight.groundColor, hemiLight.skyColor, hemiDiffuseWeight );
		return irradiance;
	}
#endif
#include <lightprobes_pars_fragment>`,yb=`#ifdef USE_ENVMAP
	vec3 getIBLIrradiance( const in vec3 normal ) {
		#ifdef ENVMAP_TYPE_CUBE_UV
			vec3 worldNormal = transformNormalByInverseViewMatrix( normal, viewMatrix );
			vec4 envMapColor = textureCubeUV( envMap, envMapRotation * worldNormal, 1.0 );
			return PI * envMapColor.rgb * envMapIntensity;
		#else
			return vec3( 0.0 );
		#endif
	}
	vec3 getIBLRadiance( const in vec3 viewDir, const in vec3 normal, const in float roughness ) {
		#ifdef ENVMAP_TYPE_CUBE_UV
			vec3 reflectVec = reflect( - viewDir, normal );
			reflectVec = normalize( mix( reflectVec, normal, pow4( roughness ) ) );
			reflectVec = transformDirectionByInverseViewMatrix( reflectVec, viewMatrix );
			vec4 envMapColor = textureCubeUV( envMap, envMapRotation * reflectVec, roughness );
			return envMapColor.rgb * envMapIntensity;
		#else
			return vec3( 0.0 );
		#endif
	}
	#ifdef USE_RETROREFLECTION
		vec3 getIBLRetroRadiance( const in vec3 viewDir, const in vec3 normal, const in float roughness ) {
			#ifdef ENVMAP_TYPE_CUBE_UV
				vec3 retroVec = normalize( mix( viewDir, normal, pow4( roughness ) ) );
				retroVec = transformDirectionByInverseViewMatrix( retroVec, viewMatrix );
				vec4 envMapColor = textureCubeUV( envMap, envMapRotation * retroVec, roughness );
				return envMapColor.rgb * envMapIntensity;
			#else
				return vec3( 0.0 );
			#endif
		}
	#endif
	#ifdef USE_ANISOTROPY
		vec3 getIBLAnisotropyRadiance( const in vec3 viewDir, const in vec3 normal, const in float roughness, const in vec3 bitangent, const in float anisotropy ) {
			#ifdef ENVMAP_TYPE_CUBE_UV
				vec3 bentNormal = cross( bitangent, viewDir );
				bentNormal = normalize( cross( bentNormal, bitangent ) );
				bentNormal = normalize( mix( bentNormal, normal, pow2( pow2( 1.0 - anisotropy * ( 1.0 - roughness ) ) ) ) );
				return getIBLRadiance( viewDir, bentNormal, roughness );
			#else
				return vec3( 0.0 );
			#endif
		}
		#ifdef USE_RETROREFLECTION
			vec3 getIBLAnisotropyRetroRadiance( const in vec3 viewDir, const in vec3 normal, const in float roughness, const in vec3 bitangent, const in float anisotropy ) {
				#ifdef ENVMAP_TYPE_CUBE_UV
					vec3 bentNormal = cross( bitangent, viewDir );
					bentNormal = normalize( cross( bentNormal, bitangent ) );
					bentNormal = normalize( mix( bentNormal, normal, pow2( pow2( 1.0 - anisotropy * ( 1.0 - roughness ) ) ) ) );
					return getIBLRetroRadiance( viewDir, bentNormal, roughness );
				#else
					return vec3( 0.0 );
				#endif
			}
		#endif
	#endif
#endif`,vb=`ToonMaterial material;
material.diffuseColor = diffuseColor.rgb;`,Mb=`varying vec3 vViewPosition;
struct ToonMaterial {
	vec3 diffuseColor;
};
void RE_Direct_Toon( const in IncidentLight directLight, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in ToonMaterial material, inout ReflectedLight reflectedLight ) {
	vec3 irradiance = getGradientIrradiance( geometryNormal, directLight.direction ) * directLight.color;
	reflectedLight.directDiffuse += irradiance * BRDF_Lambert( material.diffuseColor );
}
void RE_IndirectDiffuse_Toon( const in vec3 irradiance, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in ToonMaterial material, inout ReflectedLight reflectedLight ) {
	reflectedLight.indirectDiffuse += irradiance * BRDF_Lambert( material.diffuseColor );
}
#define RE_Direct				RE_Direct_Toon
#define RE_IndirectDiffuse		RE_IndirectDiffuse_Toon`,Sb=`BlinnPhongMaterial material;
material.diffuseColor = diffuseColor.rgb;
material.specularColor = specular;
material.specularShininess = shininess;
material.specularStrength = specularStrength;`,Eb=`varying vec3 vViewPosition;
struct BlinnPhongMaterial {
	vec3 diffuseColor;
	vec3 specularColor;
	float specularShininess;
	float specularStrength;
};
void RE_Direct_BlinnPhong( const in IncidentLight directLight, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in BlinnPhongMaterial material, inout ReflectedLight reflectedLight ) {
	float dotNL = saturate( dot( geometryNormal, directLight.direction ) );
	vec3 irradiance = dotNL * directLight.color;
	reflectedLight.directDiffuse += irradiance * BRDF_Lambert( material.diffuseColor );
	reflectedLight.directSpecular += irradiance * BRDF_BlinnPhong( directLight.direction, geometryViewDir, geometryNormal, material.specularColor, material.specularShininess ) * material.specularStrength;
}
void RE_IndirectDiffuse_BlinnPhong( const in vec3 irradiance, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in BlinnPhongMaterial material, inout ReflectedLight reflectedLight ) {
	reflectedLight.indirectDiffuse += irradiance * BRDF_Lambert( material.diffuseColor );
}
#define RE_Direct				RE_Direct_BlinnPhong
#define RE_IndirectDiffuse		RE_IndirectDiffuse_BlinnPhong`,Tb=`PhysicalMaterial material;
material.diffuseColor = diffuseColor.rgb;
material.diffuseContribution = diffuseColor.rgb * ( 1.0 - metalnessFactor );
material.metalness = metalnessFactor;
vec3 dxy = max( abs( dFdx( nonPerturbedNormal ) ), abs( dFdy( nonPerturbedNormal ) ) );
float geometryRoughness = max( max( dxy.x, dxy.y ), dxy.z );
material.roughness = max( roughnessFactor, 0.0525 );material.roughness += geometryRoughness;
material.roughness = min( material.roughness, 1.0 );
#ifdef IOR
	material.ior = ior;
	#ifdef USE_SPECULAR
		float specularIntensityFactor = specularIntensity;
		vec3 specularColorFactor = specularColor;
		#ifdef USE_SPECULAR_COLORMAP
			specularColorFactor *= texture2D( specularColorMap, vSpecularColorMapUv ).rgb;
		#endif
		#ifdef USE_SPECULAR_INTENSITYMAP
			specularIntensityFactor *= texture2D( specularIntensityMap, vSpecularIntensityMapUv ).a;
		#endif
		material.specularF90 = mix( specularIntensityFactor, 1.0, metalnessFactor );
	#else
		float specularIntensityFactor = 1.0;
		vec3 specularColorFactor = vec3( 1.0 );
		material.specularF90 = 1.0;
	#endif
	material.specularColor = min( pow2( ( material.ior - 1.0 ) / ( material.ior + 1.0 ) ) * specularColorFactor, vec3( 1.0 ) ) * specularIntensityFactor;
	material.specularColorBlended = mix( material.specularColor, diffuseColor.rgb, metalnessFactor );
#else
	material.specularColor = vec3( 0.04 );
	material.specularColorBlended = mix( material.specularColor, diffuseColor.rgb, metalnessFactor );
	material.specularF90 = 1.0;
#endif
#ifdef USE_CLEARCOAT
	material.clearcoat = clearcoat;
	material.clearcoatRoughness = clearcoatRoughness;
	material.clearcoatF0 = vec3( 0.04 );
	material.clearcoatF90 = 1.0;
	#ifdef USE_CLEARCOATMAP
		material.clearcoat *= texture2D( clearcoatMap, vClearcoatMapUv ).x;
	#endif
	#ifdef USE_CLEARCOAT_ROUGHNESSMAP
		material.clearcoatRoughness *= texture2D( clearcoatRoughnessMap, vClearcoatRoughnessMapUv ).y;
	#endif
	material.clearcoat = saturate( material.clearcoat );	material.clearcoatRoughness = max( material.clearcoatRoughness, 0.0525 );
	material.clearcoatRoughness += geometryRoughness;
	material.clearcoatRoughness = min( material.clearcoatRoughness, 1.0 );
#endif
#ifdef USE_DISPERSION
	material.dispersion = dispersion;
#endif
#ifdef USE_RETROREFLECTION
	material.retroreflectivity = retroreflectivity;
#endif
#ifdef USE_IRIDESCENCE
	material.iridescence = iridescence;
	material.iridescenceIOR = iridescenceIOR;
	#ifdef USE_IRIDESCENCEMAP
		material.iridescence *= texture2D( iridescenceMap, vIridescenceMapUv ).r;
	#endif
	#ifdef USE_IRIDESCENCE_THICKNESSMAP
		material.iridescenceThickness = (iridescenceThicknessMaximum - iridescenceThicknessMinimum) * texture2D( iridescenceThicknessMap, vIridescenceThicknessMapUv ).g + iridescenceThicknessMinimum;
	#else
		material.iridescenceThickness = iridescenceThicknessMaximum;
	#endif
#endif
#ifdef USE_SHEEN
	material.sheenColor = sheenColor;
	#ifdef USE_SHEEN_COLORMAP
		material.sheenColor *= texture2D( sheenColorMap, vSheenColorMapUv ).rgb;
	#endif
	material.sheenRoughness = clamp( sheenRoughness, 0.0001, 1.0 );
	#ifdef USE_SHEEN_ROUGHNESSMAP
		material.sheenRoughness *= texture2D( sheenRoughnessMap, vSheenRoughnessMapUv ).a;
	#endif
#endif
#ifdef USE_ANISOTROPY
	#ifdef USE_ANISOTROPYMAP
		mat2 anisotropyMat = mat2( anisotropyVector.x, anisotropyVector.y, - anisotropyVector.y, anisotropyVector.x );
		vec3 anisotropyPolar = texture2D( anisotropyMap, vAnisotropyMapUv ).rgb;
		vec2 anisotropyV = anisotropyMat * normalize( 2.0 * anisotropyPolar.rg - vec2( 1.0 ) ) * anisotropyPolar.b;
	#else
		vec2 anisotropyV = anisotropyVector;
	#endif
	material.anisotropy = length( anisotropyV );
	if( material.anisotropy == 0.0 ) {
		anisotropyV = vec2( 1.0, 0.0 );
	} else {
		anisotropyV /= material.anisotropy;
		material.anisotropy = saturate( material.anisotropy );
	}
	material.alphaT = mix( pow2( material.roughness ), 1.0, pow2( material.anisotropy ) );
	material.anisotropyT = tbn[ 0 ] * anisotropyV.x + tbn[ 1 ] * anisotropyV.y;
	material.anisotropyB = tbn[ 1 ] * anisotropyV.x - tbn[ 0 ] * anisotropyV.y;
#endif`,wb=`uniform sampler2D dfgLUT;
struct PhysicalMaterial {
	vec3 diffuseColor;
	vec3 diffuseContribution;
	vec3 specularColor;
	vec3 specularColorBlended;
	float roughness;
	float metalness;
	float specularF90;
	float dispersion;
	vec2 dfg;
	vec3 multiScatteringCompensation;
	#ifdef USE_RETROREFLECTION
		float retroreflectivity;
	#endif
	#ifdef USE_CLEARCOAT
		float clearcoat;
		float clearcoatRoughness;
		vec3 clearcoatF0;
		float clearcoatF90;
	#endif
	#ifdef USE_IRIDESCENCE
		float iridescence;
		float iridescenceIOR;
		float iridescenceThickness;
		vec3 iridescenceFresnel;
		vec3 iridescenceF0Dielectric;
		vec3 iridescenceF0Metallic;
	#endif
	#ifdef USE_SHEEN
		vec3 sheenColor;
		float sheenRoughness;
	#endif
	#ifdef IOR
		float ior;
	#endif
	#ifdef USE_TRANSMISSION
		float transmission;
		float transmissionAlpha;
		float thickness;
		float attenuationDistance;
		vec3 attenuationColor;
	#endif
	#ifdef USE_ANISOTROPY
		float anisotropy;
		float alphaT;
		vec3 anisotropyT;
		vec3 anisotropyB;
	#endif
};
vec3 clearcoatSpecularDirect = vec3( 0.0 );
vec3 clearcoatSpecularIndirect = vec3( 0.0 );
vec3 sheenSpecularDirect = vec3( 0.0 );
vec3 sheenSpecularIndirect = vec3(0.0 );
vec3 Schlick_to_F0( const in vec3 f, const in float f90, const in float dotVH ) {
    float x = clamp( 1.0 - dotVH, 0.0, 1.0 );
    float x2 = x * x;
    float x5 = clamp( x * x2 * x2, 0.0, 0.9999 );
    return ( f - vec3( f90 ) * x5 ) / ( 1.0 - x5 );
}
float V_GGX_SmithCorrelated( const in float alpha, const in float dotNL, const in float dotNV ) {
	float a2 = pow2( alpha );
	float gv = dotNL * sqrt( a2 + ( 1.0 - a2 ) * pow2( dotNV ) );
	float gl = dotNV * sqrt( a2 + ( 1.0 - a2 ) * pow2( dotNL ) );
	return 0.5 / max( gv + gl, EPSILON );
}
float D_GGX( const in float alpha, const in float dotNH ) {
	float a2 = pow2( alpha );
	float denom = pow2( dotNH ) * ( a2 - 1.0 ) + 1.0;
	return RECIPROCAL_PI * a2 / pow2( denom );
}
#ifdef USE_ANISOTROPY
	float V_GGX_SmithCorrelated_Anisotropic( const in float alphaT, const in float alphaB, const in float dotTV, const in float dotBV, const in float dotTL, const in float dotBL, const in float dotNV, const in float dotNL ) {
		float gv = dotNL * length( vec3( alphaT * dotTV, alphaB * dotBV, dotNV ) );
		float gl = dotNV * length( vec3( alphaT * dotTL, alphaB * dotBL, dotNL ) );
		return 0.5 / max( gv + gl, EPSILON );
	}
	float D_GGX_Anisotropic( const in float alphaT, const in float alphaB, const in float dotNH, const in float dotTH, const in float dotBH ) {
		float a2 = alphaT * alphaB;
		highp vec3 v = vec3( alphaB * dotTH, alphaT * dotBH, a2 * dotNH );
		highp float v2 = dot( v, v );
		float w2 = a2 / v2;
		return RECIPROCAL_PI * a2 * pow2 ( w2 );
	}
#endif
#ifdef USE_CLEARCOAT
	vec3 BRDF_GGX_Clearcoat( const in vec3 lightDir, const in vec3 viewDir, const in vec3 normal, const in PhysicalMaterial material) {
		vec3 f0 = material.clearcoatF0;
		float f90 = material.clearcoatF90;
		float roughness = material.clearcoatRoughness;
		float alpha = pow2( roughness );
		vec3 halfDir = normalize( lightDir + viewDir );
		float dotNL = saturate( dot( normal, lightDir ) );
		float dotNV = saturate( dot( normal, viewDir ) );
		float dotNH = saturate( dot( normal, halfDir ) );
		float dotVH = saturate( dot( viewDir, halfDir ) );
		vec3 F = F_Schlick( f0, f90, dotVH );
		float V = V_GGX_SmithCorrelated( alpha, dotNL, dotNV );
		float D = D_GGX( alpha, dotNH );
		return F * ( V * D );
	}
#endif
vec3 BRDF_GGX( const in vec3 lightDir, const in vec3 viewDir, const in vec3 normal, const in PhysicalMaterial material ) {
	vec3 f0 = material.specularColorBlended;
	float f90 = material.specularF90;
	float roughness = material.roughness;
	float alpha = pow2( roughness );
	vec3 halfDir = normalize( lightDir + viewDir );
	float dotNL = saturate( dot( normal, lightDir ) );
	float dotNV = saturate( dot( normal, viewDir ) );
	float dotNH = saturate( dot( normal, halfDir ) );
	float dotVH = saturate( dot( viewDir, halfDir ) );
	vec3 F = F_Schlick( f0, f90, dotVH );
	#ifdef USE_IRIDESCENCE
		F = mix( F, material.iridescenceFresnel, material.iridescence );
	#endif
	#ifdef USE_ANISOTROPY
		float dotTL = dot( material.anisotropyT, lightDir );
		float dotTV = dot( material.anisotropyT, viewDir );
		float dotTH = dot( material.anisotropyT, halfDir );
		float dotBL = dot( material.anisotropyB, lightDir );
		float dotBV = dot( material.anisotropyB, viewDir );
		float dotBH = dot( material.anisotropyB, halfDir );
		float V = V_GGX_SmithCorrelated_Anisotropic( material.alphaT, alpha, dotTV, dotBV, dotTL, dotBL, dotNV, dotNL );
		float D = D_GGX_Anisotropic( material.alphaT, alpha, dotNH, dotTH, dotBH );
	#else
		float V = V_GGX_SmithCorrelated( alpha, dotNL, dotNV );
		float D = D_GGX( alpha, dotNH );
	#endif
	return F * ( V * D );
}
vec2 LTC_Uv( const in vec3 N, const in vec3 V, const in float roughness ) {
	const float LUT_SIZE = 64.0;
	const float LUT_SCALE = ( LUT_SIZE - 1.0 ) / LUT_SIZE;
	const float LUT_BIAS = 0.5 / LUT_SIZE;
	float dotNV = saturate( dot( N, V ) );
	vec2 uv = vec2( roughness, sqrt( 1.0 - dotNV ) );
	uv = uv * LUT_SCALE + LUT_BIAS;
	return uv;
}
float LTC_ClippedSphereFormFactor( const in vec3 f ) {
	float l = length( f );
	return max( ( l * l + f.z ) / ( l + 1.0 ), 0.0 );
}
vec3 LTC_EdgeVectorFormFactor( const in vec3 v1, const in vec3 v2 ) {
	float x = dot( v1, v2 );
	float y = abs( x );
	float a = 0.8543985 + ( 0.4965155 + 0.0145206 * y ) * y;
	float b = 3.4175940 + ( 4.1616724 + y ) * y;
	float v = a / b;
	float theta_sintheta = ( x > 0.0 ) ? v : 0.5 * inversesqrt( max( 1.0 - x * x, 1e-7 ) ) - v;
	return cross( v1, v2 ) * theta_sintheta;
}
vec3 LTC_Evaluate( const in vec3 N, const in vec3 V, const in vec3 P, const in mat3 mInv, const in vec3 rectCoords[ 4 ] ) {
	vec3 v1 = rectCoords[ 1 ] - rectCoords[ 0 ];
	vec3 v2 = rectCoords[ 3 ] - rectCoords[ 0 ];
	vec3 lightNormal = cross( v1, v2 );
	if( dot( lightNormal, P - rectCoords[ 0 ] ) < 0.0 ) return vec3( 0.0 );
	vec3 T1, T2;
	T1 = normalize( V - N * dot( V, N ) );
	T2 = - cross( N, T1 );
	mat3 mat = mInv * transpose( mat3( T1, T2, N ) );
	vec3 coords[ 4 ];
	coords[ 0 ] = mat * ( rectCoords[ 0 ] - P );
	coords[ 1 ] = mat * ( rectCoords[ 1 ] - P );
	coords[ 2 ] = mat * ( rectCoords[ 2 ] - P );
	coords[ 3 ] = mat * ( rectCoords[ 3 ] - P );
	coords[ 0 ] = normalize( coords[ 0 ] );
	coords[ 1 ] = normalize( coords[ 1 ] );
	coords[ 2 ] = normalize( coords[ 2 ] );
	coords[ 3 ] = normalize( coords[ 3 ] );
	vec3 vectorFormFactor = vec3( 0.0 );
	vectorFormFactor += LTC_EdgeVectorFormFactor( coords[ 0 ], coords[ 1 ] );
	vectorFormFactor += LTC_EdgeVectorFormFactor( coords[ 1 ], coords[ 2 ] );
	vectorFormFactor += LTC_EdgeVectorFormFactor( coords[ 2 ], coords[ 3 ] );
	vectorFormFactor += LTC_EdgeVectorFormFactor( coords[ 3 ], coords[ 0 ] );
	float result = LTC_ClippedSphereFormFactor( vectorFormFactor );
	return vec3( result );
}
#if defined( USE_SHEEN )
float D_Charlie( float roughness, float dotNH ) {
	float alpha = pow2( roughness );
	float invAlpha = 1.0 / alpha;
	float cos2h = dotNH * dotNH;
	float sin2h = max( 1.0 - cos2h, 0.0078125 );
	return ( 2.0 + invAlpha ) * pow( sin2h, invAlpha * 0.5 ) / ( 2.0 * PI );
}
float V_Neubelt( float dotNV, float dotNL ) {
	return saturate( 1.0 / ( 4.0 * ( dotNL + dotNV - dotNL * dotNV ) ) );
}
vec3 BRDF_Sheen( const in vec3 lightDir, const in vec3 viewDir, const in vec3 normal, vec3 sheenColor, const in float sheenRoughness ) {
	vec3 halfDir = normalize( lightDir + viewDir );
	float dotNL = saturate( dot( normal, lightDir ) );
	float dotNV = saturate( dot( normal, viewDir ) );
	float dotNH = saturate( dot( normal, halfDir ) );
	float D = D_Charlie( sheenRoughness, dotNH );
	float V = V_Neubelt( dotNV, dotNL );
	return sheenColor * ( D * V );
}
#endif
float IBLSheenBRDF( const in vec3 normal, const in vec3 viewDir, const in float roughness ) {
	float dotNV = saturate( dot( normal, viewDir ) );
	float r2 = roughness * roughness;
	float rInv = 1.0 / ( roughness + 0.1 );
	float a = -1.9362 + 1.0678 * roughness + 0.4573 * r2 - 0.8469 * rInv;
	float b = -0.6014 + 0.5538 * roughness - 0.4670 * r2 - 0.1255 * rInv;
	float DG = exp( a * dotNV + b );
	return saturate( DG );
}
vec3 EnvironmentBRDF( const in vec3 normal, const in vec3 viewDir, const in vec3 specularColor, const in float specularF90, const in float roughness ) {
	float dotNV = saturate( dot( normal, viewDir ) );
	vec2 fab = texture2D( dfgLUT, vec2( roughness, dotNV ) ).rg;
	return specularColor * fab.x + specularF90 * fab.y;
}
#ifdef USE_IRIDESCENCE
void computeMultiscatteringIridescence( const in vec2 fab, const in vec3 specularColor, const in float specularF90, const in float iridescence, const in vec3 iridescenceF0, inout vec3 singleScatter, inout vec3 multiScatter ) {
#else
void computeMultiscattering( const in vec2 fab, const in vec3 specularColor, const in float specularF90, inout vec3 singleScatter, inout vec3 multiScatter ) {
#endif
	#ifdef USE_IRIDESCENCE
		vec3 Fr = mix( specularColor, iridescenceF0, iridescence );
	#else
		vec3 Fr = specularColor;
	#endif
	vec3 FssEss = Fr * fab.x + specularF90 * fab.y;
	float Ess = fab.x + fab.y;
	float Ems = 1.0 - Ess;
	vec3 Favg = Fr + ( 1.0 - Fr ) * 0.047619;	vec3 Fms = FssEss * Favg / ( 1.0 - Ems * Favg );
	singleScatter += FssEss;
	multiScatter += Fms * Ems;
}
#if NUM_RECT_AREA_LIGHTS > 0
	void RE_Direct_RectArea_Physical( const in RectAreaLight rectAreaLight, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in PhysicalMaterial material, inout ReflectedLight reflectedLight ) {
		vec3 normal = geometryNormal;
		vec3 viewDir = geometryViewDir;
		vec3 position = geometryPosition;
		vec3 lightPos = rectAreaLight.position;
		vec3 halfWidth = rectAreaLight.halfWidth;
		vec3 halfHeight = rectAreaLight.halfHeight;
		vec3 lightColor = rectAreaLight.color;
		float roughness = material.roughness;
		vec3 rectCoords[ 4 ];
		rectCoords[ 0 ] = lightPos + halfWidth - halfHeight;		rectCoords[ 1 ] = lightPos - halfWidth - halfHeight;
		rectCoords[ 2 ] = lightPos - halfWidth + halfHeight;
		rectCoords[ 3 ] = lightPos + halfWidth + halfHeight;
		vec2 uv = LTC_Uv( normal, viewDir, roughness );
		vec4 t1 = texture2D( ltc_1, uv );
		vec4 t2 = texture2D( ltc_2, uv );
		mat3 mInv = mat3(
			vec3( t1.x, 0, t1.y ),
			vec3(    0, 1,    0 ),
			vec3( t1.z, 0, t1.w )
		);
		vec3 fresnel = ( material.specularColorBlended * t2.x + ( material.specularF90 - material.specularColorBlended ) * t2.y );
		reflectedLight.directSpecular += lightColor * fresnel * LTC_Evaluate( normal, viewDir, position, mInv, rectCoords );
		reflectedLight.directDiffuse += lightColor * material.diffuseContribution * LTC_Evaluate( normal, viewDir, position, mat3( 1.0 ), rectCoords );
		#ifdef USE_CLEARCOAT
			vec3 Ncc = geometryClearcoatNormal;
			vec2 uvClearcoat = LTC_Uv( Ncc, viewDir, material.clearcoatRoughness );
			vec4 t1Clearcoat = texture2D( ltc_1, uvClearcoat );
			vec4 t2Clearcoat = texture2D( ltc_2, uvClearcoat );
			mat3 mInvClearcoat = mat3(
				vec3( t1Clearcoat.x, 0, t1Clearcoat.y ),
				vec3(             0, 1,             0 ),
				vec3( t1Clearcoat.z, 0, t1Clearcoat.w )
			);
			vec3 fresnelClearcoat = material.clearcoatF0 * t2Clearcoat.x + ( material.clearcoatF90 - material.clearcoatF0 ) * t2Clearcoat.y;
			clearcoatSpecularDirect += lightColor * fresnelClearcoat * LTC_Evaluate( Ncc, viewDir, position, mInvClearcoat, rectCoords );
		#endif
	}
#endif
void RE_Direct_Physical( const in IncidentLight directLight, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in PhysicalMaterial material, inout ReflectedLight reflectedLight ) {
	float dotNL = saturate( dot( geometryNormal, directLight.direction ) );
	vec3 irradiance = dotNL * directLight.color;
	#ifdef USE_CLEARCOAT
		float dotNLcc = saturate( dot( geometryClearcoatNormal, directLight.direction ) );
		vec3 ccIrradiance = dotNLcc * directLight.color;
		clearcoatSpecularDirect += ccIrradiance * BRDF_GGX_Clearcoat( directLight.direction, geometryViewDir, geometryClearcoatNormal, material );
	#endif
	#ifdef USE_SHEEN
 
 		sheenSpecularDirect += irradiance * BRDF_Sheen( directLight.direction, geometryViewDir, geometryNormal, material.sheenColor, material.sheenRoughness );
 
 		float sheenAlbedoV = IBLSheenBRDF( geometryNormal, geometryViewDir, material.sheenRoughness );
 		float sheenAlbedoL = IBLSheenBRDF( geometryNormal, directLight.direction, material.sheenRoughness );
 
 		float sheenEnergyComp = 1.0 - max3( material.sheenColor ) * max( sheenAlbedoV, sheenAlbedoL );
 
 		irradiance *= sheenEnergyComp;
 
 	#endif
	vec3 specularBRDF = BRDF_GGX( directLight.direction, geometryViewDir, geometryNormal, material );
	#ifdef USE_RETROREFLECTION
		vec3 retroViewDir = reflect( - geometryViewDir, geometryNormal );
		vec3 retroSpecularBRDF = BRDF_GGX( directLight.direction, retroViewDir, geometryNormal, material );
		specularBRDF = mix( specularBRDF, retroSpecularBRDF, saturate( material.retroreflectivity ) );
	#endif
	reflectedLight.directSpecular += irradiance * specularBRDF * material.multiScatteringCompensation;
	vec3 halfDir = normalize( directLight.direction + geometryViewDir );
	float dotVH = saturate( dot( geometryViewDir, halfDir ) );
	vec3 F = F_Schlick( material.specularColor, material.specularF90, dotVH );
	#ifdef USE_RETROREFLECTION
		vec3 retroHalfDir = normalize( directLight.direction + retroViewDir );
		float dotRetroVH = saturate( dot( retroViewDir, retroHalfDir ) );
		vec3 retroF = F_Schlick( material.specularColor, material.specularF90, dotRetroVH );
		F = mix( F, retroF, saturate( material.retroreflectivity ) );
	#endif
	reflectedLight.directDiffuse += irradiance * BRDF_Lambert( material.diffuseContribution ) * ( 1.0 - F );
}
void RE_IndirectDiffuse_Physical( const in vec3 irradiance, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in PhysicalMaterial material, inout ReflectedLight reflectedLight ) {
	vec3 singleScattering = vec3( 0.0 );
	vec3 multiScattering = vec3( 0.0 );
	#ifdef USE_IRIDESCENCE
		computeMultiscatteringIridescence( material.dfg, material.specularColor, material.specularF90, material.iridescence, material.iridescenceF0Dielectric, singleScattering, multiScattering );
	#else
		computeMultiscattering( material.dfg, material.specularColor, material.specularF90, singleScattering, multiScattering );
	#endif
	vec3 diffuse = irradiance * BRDF_Lambert( material.diffuseContribution ) * ( 1.0 - singleScattering - multiScattering );
	#ifdef USE_SHEEN
		float sheenAlbedo = IBLSheenBRDF( geometryNormal, geometryViewDir, material.sheenRoughness );
		sheenSpecularIndirect += irradiance * material.sheenColor * sheenAlbedo * RECIPROCAL_PI;
		float sheenEnergyComp = 1.0 - max3( material.sheenColor ) * sheenAlbedo;
		diffuse *= sheenEnergyComp;
	#endif
	reflectedLight.indirectDiffuse += diffuse;
}
void RE_IndirectSpecular_Physical( const in vec3 radiance, const in vec3 irradiance, const in vec3 clearcoatRadiance, const in vec3 geometryPosition, const in vec3 geometryNormal, const in vec3 geometryViewDir, const in vec3 geometryClearcoatNormal, const in PhysicalMaterial material, inout ReflectedLight reflectedLight) {
	#ifdef USE_CLEARCOAT
		clearcoatSpecularIndirect += clearcoatRadiance * EnvironmentBRDF( geometryClearcoatNormal, geometryViewDir, material.clearcoatF0, material.clearcoatF90, material.clearcoatRoughness );
	#endif
	#ifdef USE_SHEEN
		sheenSpecularIndirect += irradiance * material.sheenColor * IBLSheenBRDF( geometryNormal, geometryViewDir, material.sheenRoughness ) * RECIPROCAL_PI;
 	#endif
	vec3 singleScatteringDielectric = vec3( 0.0 );
	vec3 multiScatteringDielectric = vec3( 0.0 );
	vec3 singleScatteringMetallic = vec3( 0.0 );
	vec3 multiScatteringMetallic = vec3( 0.0 );
	#ifdef USE_IRIDESCENCE
		computeMultiscatteringIridescence( material.dfg, material.specularColor, material.specularF90, material.iridescence, material.iridescenceF0Dielectric, singleScatteringDielectric, multiScatteringDielectric );
		computeMultiscatteringIridescence( material.dfg, material.diffuseColor, material.specularF90, material.iridescence, material.iridescenceF0Metallic, singleScatteringMetallic, multiScatteringMetallic );
	#else
		computeMultiscattering( material.dfg, material.specularColor, material.specularF90, singleScatteringDielectric, multiScatteringDielectric );
		computeMultiscattering( material.dfg, material.diffuseColor, material.specularF90, singleScatteringMetallic, multiScatteringMetallic );
	#endif
	vec3 singleScattering = mix( singleScatteringDielectric, singleScatteringMetallic, material.metalness );
	vec3 multiScattering = mix( multiScatteringDielectric, multiScatteringMetallic, material.metalness );
	vec3 totalScatteringDielectric = singleScatteringDielectric + multiScatteringDielectric;
	vec3 diffuse = material.diffuseContribution * ( 1.0 - totalScatteringDielectric );
	vec3 cosineWeightedIrradiance = irradiance * RECIPROCAL_PI;
	vec3 indirectSpecular = radiance * singleScattering;
	indirectSpecular += multiScattering * cosineWeightedIrradiance;
	vec3 indirectDiffuse = diffuse * cosineWeightedIrradiance;
	#ifdef USE_SHEEN
		float sheenAlbedo = IBLSheenBRDF( geometryNormal, geometryViewDir, material.sheenRoughness );
		float sheenEnergyComp = 1.0 - max3( material.sheenColor ) * sheenAlbedo;
		indirectSpecular *= sheenEnergyComp;
		indirectDiffuse *= sheenEnergyComp;
	#endif
	reflectedLight.indirectSpecular += indirectSpecular;
	reflectedLight.indirectDiffuse += indirectDiffuse;
}
#define RE_Direct				RE_Direct_Physical
#define RE_Direct_RectArea		RE_Direct_RectArea_Physical
#define RE_IndirectDiffuse		RE_IndirectDiffuse_Physical
#define RE_IndirectSpecular		RE_IndirectSpecular_Physical
float computeSpecularOcclusion( const in float dotNV, const in float ambientOcclusion, const in float roughness ) {
	return saturate( pow( dotNV + ambientOcclusion, exp2( - 16.0 * roughness - 1.0 ) ) - 1.0 + ambientOcclusion );
}`,Ab=`
vec3 geometryPosition = - vViewPosition;
vec3 geometryNormal = normal;
vec3 geometryViewDir = ( isOrthographic ) ? vec3( 0, 0, 1 ) : normalize( vViewPosition );
vec3 geometryClearcoatNormal = vec3( 0.0 );
#ifdef USE_CLEARCOAT
	geometryClearcoatNormal = clearcoatNormal;
#endif
#ifdef USE_IRIDESCENCE
	float dotNVi = saturate( dot( normal, geometryViewDir ) );
	if ( material.iridescenceThickness == 0.0 ) {
		material.iridescence = 0.0;
	} else {
		material.iridescence = saturate( material.iridescence );
	}
	if ( material.iridescence > 0.0 ) {
		vec3 iridescenceFresnelDielectric = evalIridescence( 1.0, material.iridescenceIOR, dotNVi, material.iridescenceThickness, material.specularColor );
		vec3 iridescenceFresnelMetallic = evalIridescence( 1.0, material.iridescenceIOR, dotNVi, material.iridescenceThickness, material.diffuseColor );
		material.iridescenceFresnel = mix( iridescenceFresnelDielectric, iridescenceFresnelMetallic, material.metalness );
		material.iridescenceF0Dielectric = Schlick_to_F0( iridescenceFresnelDielectric, 1.0, dotNVi );
		material.iridescenceF0Metallic = Schlick_to_F0( iridescenceFresnelMetallic, 1.0, dotNVi );
	}
#endif
#ifdef STANDARD
	float dotNVms = saturate( dot( geometryNormal, geometryViewDir ) );
	material.dfg = texture2D( dfgLUT, vec2( material.roughness, dotNVms ) ).rg;
	#if ( NUM_SUN_LIGHTS > 0 || NUM_DIR_LIGHTS > 0 || NUM_POINT_LIGHTS > 0 || NUM_SPOT_LIGHTS > 0 )
		float EssMs = material.dfg.x + material.dfg.y;
		material.multiScatteringCompensation = 1.0 + material.specularColorBlended * ( 1.0 / EssMs - 1.0 );
	#endif
#endif
IncidentLight directLight;
#if ( NUM_POINT_LIGHTS > 0 ) && defined( RE_Direct )
	PointLight pointLight;
	#if defined( USE_SHADOWMAP ) && NUM_POINT_LIGHT_SHADOWS > 0
	PointLightShadow pointLightShadow;
	#endif
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_POINT_LIGHTS; i ++ ) {
		pointLight = pointLights[ i ];
		getPointLightInfo( pointLight, geometryPosition, directLight );
		#if defined( USE_SHADOWMAP ) && ( UNROLLED_LOOP_INDEX < NUM_POINT_LIGHT_SHADOWS ) && ( defined( SHADOWMAP_TYPE_PCF ) || defined( SHADOWMAP_TYPE_BASIC ) )
		pointLightShadow = pointLightShadows[ i ];
		directLight.color *= ( directLight.visible && receiveShadow ) ? getPointShadow( pointShadowMap[ i ], pointLightShadow.shadowMapSize, pointLightShadow.shadowIntensity, pointLightShadow.shadowBias, pointLightShadow.shadowRadius, vPointShadowCoord[ i ], pointLightShadow.shadowCameraNear, pointLightShadow.shadowCameraFar ) : 1.0;
		#endif
		RE_Direct( directLight, geometryPosition, geometryNormal, geometryViewDir, geometryClearcoatNormal, material, reflectedLight );
	}
	#pragma unroll_loop_end
#endif
#if ( NUM_SPOT_LIGHTS > 0 ) && defined( RE_Direct )
	SpotLight spotLight;
	vec4 spotColor;
	vec3 spotLightCoord;
	bool inSpotLightMap;
	#if defined( USE_SHADOWMAP ) && NUM_SPOT_LIGHT_SHADOWS > 0
	SpotLightShadow spotLightShadow;
	#endif
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_SPOT_LIGHTS; i ++ ) {
		spotLight = spotLights[ i ];
		getSpotLightInfo( spotLight, geometryPosition, directLight );
		#if ( UNROLLED_LOOP_INDEX < NUM_SPOT_LIGHT_SHADOWS_WITH_MAPS )
		#define SPOT_LIGHT_MAP_INDEX UNROLLED_LOOP_INDEX
		#elif ( UNROLLED_LOOP_INDEX < NUM_SPOT_LIGHT_SHADOWS )
		#define SPOT_LIGHT_MAP_INDEX NUM_SPOT_LIGHT_MAPS
		#else
		#define SPOT_LIGHT_MAP_INDEX ( UNROLLED_LOOP_INDEX - NUM_SPOT_LIGHT_SHADOWS + NUM_SPOT_LIGHT_SHADOWS_WITH_MAPS )
		#endif
		#if ( SPOT_LIGHT_MAP_INDEX < NUM_SPOT_LIGHT_MAPS )
			spotLightCoord = vSpotLightCoord[ i ].xyz / vSpotLightCoord[ i ].w;
			inSpotLightMap = all( lessThan( abs( spotLightCoord * 2. - 1. ), vec3( 1.0 ) ) );
			spotColor = texture2D( spotLightMap[ SPOT_LIGHT_MAP_INDEX ], spotLightCoord.xy );
			directLight.color = inSpotLightMap ? directLight.color * spotColor.rgb : directLight.color;
		#endif
		#undef SPOT_LIGHT_MAP_INDEX
		#if defined( USE_SHADOWMAP ) && ( UNROLLED_LOOP_INDEX < NUM_SPOT_LIGHT_SHADOWS )
		spotLightShadow = spotLightShadows[ i ];
		directLight.color *= ( directLight.visible && receiveShadow ) ? getShadow( spotShadowMap[ i ], spotLightShadow.shadowMapSize, spotLightShadow.shadowIntensity, spotLightShadow.shadowBias, spotLightShadow.shadowRadius, vSpotLightCoord[ i ] ) : 1.0;
		#endif
		RE_Direct( directLight, geometryPosition, geometryNormal, geometryViewDir, geometryClearcoatNormal, material, reflectedLight );
	}
	#pragma unroll_loop_end
#endif
#if ( NUM_SUN_LIGHTS > 0 ) && defined( RE_Direct )
	SunLight sunLight;
	#if defined( USE_SHADOWMAP ) && NUM_SUN_LIGHT_SHADOWS > 0
	SunLightShadow sunLightShadow;
	#endif
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_SUN_LIGHTS; i ++ ) {
		sunLight = sunLights[ i ];
		getSunLightInfo( sunLight, directLight );
		#if defined( USE_SHADOWMAP ) && ( UNROLLED_LOOP_INDEX < NUM_SUN_LIGHT_SHADOWS )
		sunLightShadow = sunLightShadows[ i ];
		directLight.color *= ( directLight.visible && receiveShadow ) ? getSunShadow( sunShadowMap[ i ], sunLightShadow, UNROLLED_LOOP_INDEX ) : 1.0;
		#endif
		RE_Direct( directLight, geometryPosition, geometryNormal, geometryViewDir, geometryClearcoatNormal, material, reflectedLight );
	}
	#pragma unroll_loop_end
#endif
#if ( NUM_DIR_LIGHTS > 0 ) && defined( RE_Direct )
	DirectionalLight directionalLight;
	#if defined( USE_SHADOWMAP ) && NUM_DIR_LIGHT_SHADOWS > 0
	DirectionalLightShadow directionalLightShadow;
	#endif
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_DIR_LIGHTS; i ++ ) {
		directionalLight = directionalLights[ i ];
		getDirectionalLightInfo( directionalLight, directLight );
		#if defined( USE_SHADOWMAP ) && ( UNROLLED_LOOP_INDEX < NUM_DIR_LIGHT_SHADOWS )
		directionalLightShadow = directionalLightShadows[ i ];
		directLight.color *= ( directLight.visible && receiveShadow ) ? getShadow( directionalShadowMap[ i ], directionalLightShadow.shadowMapSize, directionalLightShadow.shadowIntensity, directionalLightShadow.shadowBias, directionalLightShadow.shadowRadius, vDirectionalShadowCoord[ i ] ) : 1.0;
		#endif
		RE_Direct( directLight, geometryPosition, geometryNormal, geometryViewDir, geometryClearcoatNormal, material, reflectedLight );
	}
	#pragma unroll_loop_end
#endif
#if ( NUM_RECT_AREA_LIGHTS > 0 ) && defined( RE_Direct_RectArea )
	RectAreaLight rectAreaLight;
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_RECT_AREA_LIGHTS; i ++ ) {
		rectAreaLight = rectAreaLights[ i ];
		RE_Direct_RectArea( rectAreaLight, geometryPosition, geometryNormal, geometryViewDir, geometryClearcoatNormal, material, reflectedLight );
	}
	#pragma unroll_loop_end
#endif
#if defined( RE_IndirectDiffuse )
	vec3 iblIrradiance = vec3( 0.0 );
	vec3 irradiance = getAmbientLightIrradiance( ambientLightColor );
	#if defined( USE_LIGHT_PROBES )
		irradiance += getLightProbeIrradiance( lightProbe, geometryNormal );
	#endif
	#if ( NUM_HEMI_LIGHTS > 0 )
		#pragma unroll_loop_start
		for ( int i = 0; i < NUM_HEMI_LIGHTS; i ++ ) {
			irradiance += getHemisphereLightIrradiance( hemisphereLights[ i ], geometryNormal );
		}
		#pragma unroll_loop_end
	#endif
	#ifdef USE_LIGHT_PROBES_GRID
		vec3 probeWorldPos = ( ( vec4( geometryPosition, 1.0 ) - viewMatrix[ 3 ] ) * viewMatrix ).xyz;
		vec3 probeWorldNormal = transformNormalByInverseViewMatrix( geometryNormal, viewMatrix );
		irradiance += getLightProbeGridIrradiance( probeWorldPos, probeWorldNormal );
	#endif
#endif
#if defined( RE_IndirectSpecular )
	vec3 radiance = vec3( 0.0 );
	vec3 clearcoatRadiance = vec3( 0.0 );
#endif`,Rb=`#if defined( RE_IndirectDiffuse )
	#ifdef USE_LIGHTMAP
		vec4 lightMapTexel = texture2D( lightMap, vLightMapUv );
		vec3 lightMapIrradiance = lightMapTexel.rgb * lightMapIntensity;
		irradiance += lightMapIrradiance;
	#endif
	#if defined( USE_ENVMAP ) && defined( ENVMAP_TYPE_CUBE_UV )
		#if defined( STANDARD ) || defined( LAMBERT ) || defined( PHONG )
			iblIrradiance += getIBLIrradiance( geometryNormal );
		#endif
	#endif
#endif
#if defined( USE_ENVMAP ) && defined( RE_IndirectSpecular )
	#ifdef USE_ANISOTROPY
		vec3 iblRadiance = getIBLAnisotropyRadiance( geometryViewDir, geometryNormal, material.roughness, material.anisotropyB, material.anisotropy );
	#else
		vec3 iblRadiance = getIBLRadiance( geometryViewDir, geometryNormal, material.roughness );
	#endif
	#ifdef USE_RETROREFLECTION
		#ifdef USE_ANISOTROPY
			vec3 retroIBLRadiance = getIBLAnisotropyRetroRadiance( geometryViewDir, geometryNormal, material.roughness, material.anisotropyB, material.anisotropy );
		#else
			vec3 retroIBLRadiance = getIBLRetroRadiance( geometryViewDir, geometryNormal, material.roughness );
		#endif
		iblRadiance = mix( iblRadiance, retroIBLRadiance, saturate( material.retroreflectivity ) );
	#endif
	radiance += iblRadiance;
	#ifdef USE_CLEARCOAT
		clearcoatRadiance += getIBLRadiance( geometryViewDir, geometryClearcoatNormal, material.clearcoatRoughness );
	#endif
#endif`,Cb=`#if defined( RE_IndirectDiffuse )
	#if defined( LAMBERT ) || defined( PHONG )
		irradiance += iblIrradiance;
	#endif
	RE_IndirectDiffuse( irradiance, geometryPosition, geometryNormal, geometryViewDir, geometryClearcoatNormal, material, reflectedLight );
#endif
#if defined( RE_IndirectSpecular )
	RE_IndirectSpecular( radiance, iblIrradiance, clearcoatRadiance, geometryPosition, geometryNormal, geometryViewDir, geometryClearcoatNormal, material, reflectedLight );
#endif`,Pb=`#ifdef USE_LIGHT_PROBES_GRID
uniform highp sampler3D probesSH;
uniform vec3 probesMin;
uniform vec3 probesMax;
uniform vec3 probesResolution;
vec3 getLightProbeGridIrradiance( vec3 worldPos, vec3 worldNormal ) {
	vec3 res = probesResolution;
	vec3 gridRange = probesMax - probesMin;
	vec3 resMinusOne = res - 1.0;
	vec3 probeSpacing = gridRange / resMinusOne;
	vec3 samplePos = worldPos + worldNormal * probeSpacing * 0.5;
	vec3 uvw = clamp( ( samplePos - probesMin ) / gridRange, 0.0, 1.0 );
	uvw = uvw * resMinusOne / res + 0.5 / res;
	float nz          = res.z;
	float paddedSlices = nz + 2.0;
	float atlasDepth  = 7.0 * paddedSlices;
	float uvZBase     = uvw.z * nz + 1.0;
	vec4 s0 = texture( probesSH, vec3( uvw.xy, ( uvZBase                       ) / atlasDepth ) );
	vec4 s1 = texture( probesSH, vec3( uvw.xy, ( uvZBase +       paddedSlices   ) / atlasDepth ) );
	vec4 s2 = texture( probesSH, vec3( uvw.xy, ( uvZBase + 2.0 * paddedSlices   ) / atlasDepth ) );
	vec4 s3 = texture( probesSH, vec3( uvw.xy, ( uvZBase + 3.0 * paddedSlices   ) / atlasDepth ) );
	vec4 s4 = texture( probesSH, vec3( uvw.xy, ( uvZBase + 4.0 * paddedSlices   ) / atlasDepth ) );
	vec4 s5 = texture( probesSH, vec3( uvw.xy, ( uvZBase + 5.0 * paddedSlices   ) / atlasDepth ) );
	vec4 s6 = texture( probesSH, vec3( uvw.xy, ( uvZBase + 6.0 * paddedSlices   ) / atlasDepth ) );
	vec3 c0 = s0.xyz;
	vec3 c1 = vec3( s0.w, s1.xy );
	vec3 c2 = vec3( s1.zw, s2.x );
	vec3 c3 = s2.yzw;
	vec3 c4 = s3.xyz;
	vec3 c5 = vec3( s3.w, s4.xy );
	vec3 c6 = vec3( s4.zw, s5.x );
	vec3 c7 = s5.yzw;
	vec3 c8 = s6.xyz;
	float x = worldNormal.x, y = worldNormal.y, z = worldNormal.z;
	vec3 result = c0 * 0.886227;
	result += c1 * 2.0 * 0.511664 * y;
	result += c2 * 2.0 * 0.511664 * z;
	result += c3 * 2.0 * 0.511664 * x;
	result += c4 * 2.0 * 0.429043 * x * y;
	result += c5 * 2.0 * 0.429043 * y * z;
	result += c6 * ( 0.743125 * z * z - 0.247708 );
	result += c7 * 2.0 * 0.429043 * x * z;
	result += c8 * 0.429043 * ( x * x - y * y );
	return max( result, vec3( 0.0 ) );
}
#endif`,Ib=`#if defined( USE_LOGARITHMIC_DEPTH_BUFFER )
	gl_FragDepth = vIsPerspective == 0.0 ? gl_FragCoord.z : log2( vFragDepth ) * logDepthBufFC * 0.5;
#endif`,Lb=`#if defined( USE_LOGARITHMIC_DEPTH_BUFFER )
	uniform float logDepthBufFC;
	varying float vFragDepth;
	varying float vIsPerspective;
#endif`,Db=`#ifdef USE_LOGARITHMIC_DEPTH_BUFFER
	varying float vFragDepth;
	varying float vIsPerspective;
#endif`,Nb=`#ifdef USE_LOGARITHMIC_DEPTH_BUFFER
	vFragDepth = 1.0 + gl_Position.w;
	vIsPerspective = float( isPerspectiveMatrix( projectionMatrix ) );
#endif`,Fb=`#ifdef USE_MAP
	vec4 sampledDiffuseColor = texture2D( map, vMapUv );
	#ifdef DECODE_VIDEO_TEXTURE
		sampledDiffuseColor = sRGBTransferEOTF( sampledDiffuseColor );
	#endif
	diffuseColor *= sampledDiffuseColor;
#endif`,Ub=`#ifdef USE_MAP
	uniform sampler2D map;
#endif`,Ob=`#if defined( USE_MAP ) || defined( USE_ALPHAMAP )
	#if defined( USE_POINTS_UV )
		vec2 uv = vUv;
	#else
		vec2 uv = ( uvTransform * vec3( gl_PointCoord.x, 1.0 - gl_PointCoord.y, 1 ) ).xy;
	#endif
#endif
#ifdef USE_MAP
	diffuseColor *= texture2D( map, uv );
#endif
#ifdef USE_ALPHAMAP
	diffuseColor.a *= texture2D( alphaMap, uv ).g;
#endif`,Bb=`#if defined( USE_POINTS_UV )
	varying vec2 vUv;
#else
	#if defined( USE_MAP ) || defined( USE_ALPHAMAP )
		uniform mat3 uvTransform;
	#endif
#endif
#ifdef USE_MAP
	uniform sampler2D map;
#endif
#ifdef USE_ALPHAMAP
	uniform sampler2D alphaMap;
#endif`,kb=`float metalnessFactor = metalness;
#ifdef USE_METALNESSMAP
	vec4 texelMetalness = texture2D( metalnessMap, vMetalnessMapUv );
	metalnessFactor *= texelMetalness.b;
#endif`,zb=`#ifdef USE_METALNESSMAP
	uniform sampler2D metalnessMap;
#endif`,Hb=`#ifdef USE_INSTANCING_MORPH
	float morphTargetInfluences[ MORPHTARGETS_COUNT ];
	float morphTargetBaseInfluence = texelFetch( morphTexture, ivec2( 0, gl_InstanceID ), 0 ).r;
	for ( int i = 0; i < MORPHTARGETS_COUNT; i ++ ) {
		morphTargetInfluences[i] =  texelFetch( morphTexture, ivec2( i + 1, gl_InstanceID ), 0 ).r;
	}
#endif`,Gb=`#if defined( USE_MORPHCOLORS )
	vColor *= morphTargetBaseInfluence;
	for ( int i = 0; i < MORPHTARGETS_COUNT; i ++ ) {
		#if defined( USE_COLOR_ALPHA )
			if ( morphTargetInfluences[ i ] != 0.0 ) vColor += getMorph( gl_VertexID, i, 2 ) * morphTargetInfluences[ i ];
		#elif defined( USE_COLOR )
			if ( morphTargetInfluences[ i ] != 0.0 ) vColor += getMorph( gl_VertexID, i, 2 ).rgb * morphTargetInfluences[ i ];
		#endif
	}
#endif`,Vb=`#ifdef USE_MORPHNORMALS
	objectNormal *= morphTargetBaseInfluence;
	for ( int i = 0; i < MORPHTARGETS_COUNT; i ++ ) {
		if ( morphTargetInfluences[ i ] != 0.0 ) objectNormal += getMorph( gl_VertexID, i, 1 ).xyz * morphTargetInfluences[ i ];
	}
#endif`,Wb=`#ifdef USE_MORPHTARGETS
	#ifndef USE_INSTANCING_MORPH
		uniform float morphTargetBaseInfluence;
		uniform float morphTargetInfluences[ MORPHTARGETS_COUNT ];
	#endif
	uniform sampler2DArray morphTargetsTexture;
	uniform ivec2 morphTargetsTextureSize;
	vec4 getMorph( const in int vertexIndex, const in int morphTargetIndex, const in int offset ) {
		int texelIndex = vertexIndex * MORPHTARGETS_TEXTURE_STRIDE + offset;
		int y = texelIndex / morphTargetsTextureSize.x;
		int x = texelIndex - y * morphTargetsTextureSize.x;
		ivec3 morphUV = ivec3( x, y, morphTargetIndex );
		return texelFetch( morphTargetsTexture, morphUV, 0 );
	}
#endif`,qb=`#ifdef USE_MORPHTARGETS
	transformed *= morphTargetBaseInfluence;
	for ( int i = 0; i < MORPHTARGETS_COUNT; i ++ ) {
		if ( morphTargetInfluences[ i ] != 0.0 ) transformed += getMorph( gl_VertexID, i, 0 ).xyz * morphTargetInfluences[ i ];
	}
#endif`,Xb=`float faceDirection = gl_FrontFacing ? 1.0 : - 1.0;
#ifdef FLAT_SHADED
	vec3 fdx = dFdx( vViewPosition );
	vec3 fdy = dFdy( vViewPosition );
	vec3 normal = normalize( cross( fdx, fdy ) );
#else
	vec3 normal = normalize( vNormal );
	#ifdef DOUBLE_SIDED
		normal *= faceDirection;
	#endif
#endif
#if defined( USE_NORMALMAP_TANGENTSPACE ) || defined( USE_CLEARCOAT_NORMALMAP ) || defined( USE_ANISOTROPY )
	#ifdef USE_TANGENT
		mat3 tbn = mat3( normalize( vTangent ), normalize( vBitangent ), normal );
	#else
		mat3 tbn = getTangentFrame( - vViewPosition, normal,
		#if defined( USE_NORMALMAP )
			vNormalMapUv
		#elif defined( USE_CLEARCOAT_NORMALMAP )
			vClearcoatNormalMapUv
		#else
			vUv
		#endif
		);
	#endif
	#ifdef DOUBLE_SIDED
		tbn[0] *= faceDirection;
		tbn[1] *= faceDirection;
	#endif
#endif
#ifdef USE_CLEARCOAT_NORMALMAP
	#ifdef USE_TANGENT
		mat3 tbn2 = mat3( normalize( vTangent ), normalize( vBitangent ), normal );
	#else
		mat3 tbn2 = getTangentFrame( - vViewPosition, normal, vClearcoatNormalMapUv );
	#endif
	#ifdef DOUBLE_SIDED
		tbn2[0] *= faceDirection;
		tbn2[1] *= faceDirection;
	#endif
#endif
vec3 nonPerturbedNormal = normal;`,jb=`#ifdef USE_NORMALMAP_OBJECTSPACE
	normal = texture2D( normalMap, vNormalMapUv ).xyz * 2.0 - 1.0;
	#ifdef FLIP_SIDED
		normal = - normal;
	#endif
	#ifdef DOUBLE_SIDED
		normal = normal * faceDirection;
	#endif
	normal = normalize( normalMatrix * normal );
#elif defined( USE_NORMALMAP_TANGENTSPACE )
	vec3 mapN = texture2D( normalMap, vNormalMapUv ).xyz * 2.0 - 1.0;
	#if defined( USE_PACKED_NORMALMAP )
		mapN = vec3( mapN.xy, sqrt( saturate( 1.0 - dot( mapN.xy, mapN.xy ) ) ) );
	#endif
	mapN.xy *= normalScale;
	normal = normalize( tbn * mapN );
#elif defined( USE_BUMPMAP )
	normal = perturbNormalArb( - vViewPosition, normal, dHdxy_fwd(), faceDirection );
#endif`,Kb=`#ifndef FLAT_SHADED
	varying vec3 vNormal;
	#ifdef USE_TANGENT
		varying vec3 vTangent;
		varying vec3 vBitangent;
	#endif
#endif`,Yb=`#ifndef FLAT_SHADED
	varying vec3 vNormal;
	#ifdef USE_TANGENT
		varying vec3 vTangent;
		varying vec3 vBitangent;
	#endif
#endif`,Jb=`#ifndef FLAT_SHADED
	vNormal = normalize( transformedNormal );
	#ifdef USE_TANGENT
		vTangent = normalize( transformedTangent );
		vBitangent = normalize( cross( vNormal, vTangent ) * tangent.w );
		#ifdef FLIP_SIDED
			vBitangent = - vBitangent;
		#endif
	#endif
#endif`,Zb=`#ifdef USE_NORMALMAP
	uniform sampler2D normalMap;
	uniform vec2 normalScale;
#endif
#ifdef USE_NORMALMAP_OBJECTSPACE
	uniform mat3 normalMatrix;
#endif
#if ! defined ( USE_TANGENT ) && ( defined ( USE_NORMALMAP_TANGENTSPACE ) || defined ( USE_CLEARCOAT_NORMALMAP ) || defined( USE_ANISOTROPY ) )
	mat3 getTangentFrame( vec3 eye_pos, vec3 surf_norm, vec2 uv ) {
		vec3 q0 = dFdx( eye_pos.xyz );
		vec3 q1 = dFdy( eye_pos.xyz );
		vec2 st0 = dFdx( uv.st );
		vec2 st1 = dFdy( uv.st );
		vec3 N = surf_norm;
		vec3 q1perp = cross( q1, N );
		vec3 q0perp = cross( N, q0 );
		vec3 T = q1perp * st0.x + q0perp * st1.x;
		vec3 B = q1perp * st0.y + q0perp * st1.y;
		float det = max( dot( T, T ), dot( B, B ) );
		float scale = ( det == 0.0 ) ? 0.0 : inversesqrt( det );
		return mat3( T * scale, B * scale, N );
	}
#endif`,$b=`#ifdef USE_CLEARCOAT
	vec3 clearcoatNormal = nonPerturbedNormal;
#endif`,Qb=`#ifdef USE_CLEARCOAT_NORMALMAP
	vec3 clearcoatMapN = texture2D( clearcoatNormalMap, vClearcoatNormalMapUv ).xyz * 2.0 - 1.0;
	clearcoatMapN.xy *= clearcoatNormalScale;
	clearcoatNormal = normalize( tbn2 * clearcoatMapN );
#endif`,ex=`#ifdef USE_CLEARCOATMAP
	uniform sampler2D clearcoatMap;
#endif
#ifdef USE_CLEARCOAT_NORMALMAP
	uniform sampler2D clearcoatNormalMap;
	uniform vec2 clearcoatNormalScale;
#endif
#ifdef USE_CLEARCOAT_ROUGHNESSMAP
	uniform sampler2D clearcoatRoughnessMap;
#endif`,tx=`#ifdef USE_IRIDESCENCEMAP
	uniform sampler2D iridescenceMap;
#endif
#ifdef USE_IRIDESCENCE_THICKNESSMAP
	uniform sampler2D iridescenceThicknessMap;
#endif`,nx=`#ifdef OPAQUE
diffuseColor.a = 1.0;
#endif
#ifdef USE_TRANSMISSION
diffuseColor.a *= material.transmissionAlpha;
#endif
gl_FragColor = vec4( outgoingLight, diffuseColor.a );`,ix=`vec3 packNormalToRGB( const in vec3 normal ) {
	return normalize( normal ) * 0.5 + 0.5;
}
vec3 unpackRGBToNormal( const in vec3 rgb ) {
	return 2.0 * rgb.xyz - 1.0;
}
const float PackUpscale = 256. / 255.;const float UnpackDownscale = 255. / 256.;const float ShiftRight8 = 1. / 256.;
const float Inv255 = 1. / 255.;
const vec4 PackFactors = vec4( 1.0, 256.0, 256.0 * 256.0, 256.0 * 256.0 * 256.0 );
const vec2 UnpackFactors2 = vec2( UnpackDownscale, 1.0 / PackFactors.g );
const vec3 UnpackFactors3 = vec3( UnpackDownscale / PackFactors.rg, 1.0 / PackFactors.b );
const vec4 UnpackFactors4 = vec4( UnpackDownscale / PackFactors.rgb, 1.0 / PackFactors.a );
vec4 packDepthToRGBA( const in float v ) {
	if( v <= 0.0 )
		return vec4( 0., 0., 0., 0. );
	if( v >= 1.0 )
		return vec4( 1., 1., 1., 1. );
	float vuf;
	float af = modf( v * PackFactors.a, vuf );
	float bf = modf( vuf * ShiftRight8, vuf );
	float gf = modf( vuf * ShiftRight8, vuf );
	return vec4( vuf * Inv255, gf * PackUpscale, bf * PackUpscale, af );
}
vec3 packDepthToRGB( const in float v ) {
	if( v <= 0.0 )
		return vec3( 0., 0., 0. );
	if( v >= 1.0 )
		return vec3( 1., 1., 1. );
	float vuf;
	float bf = modf( v * PackFactors.b, vuf );
	float gf = modf( vuf * ShiftRight8, vuf );
	return vec3( vuf * Inv255, gf * PackUpscale, bf );
}
vec2 packDepthToRG( const in float v ) {
	if( v <= 0.0 )
		return vec2( 0., 0. );
	if( v >= 1.0 )
		return vec2( 1., 1. );
	float vuf;
	float gf = modf( v * 256., vuf );
	return vec2( vuf * Inv255, gf );
}
float unpackRGBAToDepth( const in vec4 v ) {
	return dot( v, UnpackFactors4 );
}
float unpackRGBToDepth( const in vec3 v ) {
	return dot( v, UnpackFactors3 );
}
float unpackRGToDepth( const in vec2 v ) {
	return v.r * UnpackFactors2.r + v.g * UnpackFactors2.g;
}
vec4 pack2HalfToRGBA( const in vec2 v ) {
	vec4 r = vec4( v.x, fract( v.x * 255.0 ), v.y, fract( v.y * 255.0 ) );
	return vec4( r.x - r.y / 255.0, r.y, r.z - r.w / 255.0, r.w );
}
vec2 unpackRGBATo2Half( const in vec4 v ) {
	return vec2( v.x + ( v.y / 255.0 ), v.z + ( v.w / 255.0 ) );
}
float viewZToOrthographicDepth( const in float viewZ, const in float near, const in float far ) {
	return ( viewZ + near ) / ( near - far );
}
float orthographicDepthToViewZ( const in float depth, const in float near, const in float far ) {
	#ifdef USE_REVERSED_DEPTH_BUFFER
	
		return depth * ( far - near ) - far;
	#else
		return depth * ( near - far ) - near;
	#endif
}
float viewZToPerspectiveDepth( const in float viewZ, const in float near, const in float far ) {
	return ( ( near + viewZ ) * far ) / ( ( far - near ) * viewZ );
}
float perspectiveDepthToViewZ( const in float depth, const in float near, const in float far ) {
	
	#ifdef USE_REVERSED_DEPTH_BUFFER
		return ( near * far ) / ( ( near - far ) * depth - near );
	#else
		return ( near * far ) / ( ( far - near ) * depth - far );
	#endif
}`,sx=`#ifdef PREMULTIPLIED_ALPHA
	gl_FragColor.rgb *= gl_FragColor.a;
#endif`,rx=`vec4 mvPosition = vec4( transformed, 1.0 );
#ifdef USE_BATCHING
	mvPosition = batchingMatrix * mvPosition;
#endif
#ifdef USE_INSTANCING
	mvPosition = instanceMatrix * mvPosition;
#endif
mvPosition = modelViewMatrix * mvPosition;
gl_Position = projectionMatrix * mvPosition;`,ax=`#ifdef DITHERING
	gl_FragColor.rgb = dithering( gl_FragColor.rgb );
#endif`,ox=`#ifdef DITHERING
	vec3 dithering( vec3 color ) {
		float grid_position = rand( gl_FragCoord.xy );
		vec3 dither_shift_RGB = vec3( 0.25 / 255.0, -0.25 / 255.0, 0.25 / 255.0 );
		dither_shift_RGB = mix( 2.0 * dither_shift_RGB, -2.0 * dither_shift_RGB, grid_position );
		return color + dither_shift_RGB;
	}
#endif`,cx=`float roughnessFactor = roughness;
#ifdef USE_ROUGHNESSMAP
	vec4 texelRoughness = texture2D( roughnessMap, vRoughnessMapUv );
	roughnessFactor *= texelRoughness.g;
#endif`,lx=`#ifdef USE_ROUGHNESSMAP
	uniform sampler2D roughnessMap;
#endif`,hx=`#if NUM_SPOT_LIGHT_COORDS > 0
	varying vec4 vSpotLightCoord[ NUM_SPOT_LIGHT_COORDS ];
#endif
#if NUM_SPOT_LIGHT_MAPS > 0
	uniform sampler2D spotLightMap[ NUM_SPOT_LIGHT_MAPS ];
#endif
#ifdef USE_SHADOWMAP
	#if NUM_SUN_LIGHT_SHADOWS > 0
		#define SUN_LIGHT_CASCADES 2
		#if defined( SHADOWMAP_TYPE_PCF )
			uniform sampler2DShadow sunShadowMap[ NUM_SUN_LIGHT_SHADOWS ];
		#else
			uniform sampler2D sunShadowMap[ NUM_SUN_LIGHT_SHADOWS ];
		#endif
		uniform mat4 sunShadowMatrix[ NUM_SUN_LIGHT_SHADOWS * SUN_LIGHT_CASCADES ];
		uniform vec4 sunShadowCascade[ NUM_SUN_LIGHT_SHADOWS * SUN_LIGHT_CASCADES ];
		varying vec4 vSunShadowWorldPosition;
		varying vec3 vSunShadowWorldNormal;
		struct SunLightShadow {
			float shadowIntensity;
			float shadowBias;
			float shadowNormalBias;
			float shadowRadius;
			vec2 shadowMapSize;
		};
		uniform SunLightShadow sunLightShadows[ NUM_SUN_LIGHT_SHADOWS ];
	#endif
	#if NUM_DIR_LIGHT_SHADOWS > 0
		#if defined( SHADOWMAP_TYPE_PCF )
			uniform sampler2DShadow directionalShadowMap[ NUM_DIR_LIGHT_SHADOWS ];
		#else
			uniform sampler2D directionalShadowMap[ NUM_DIR_LIGHT_SHADOWS ];
		#endif
		varying vec4 vDirectionalShadowCoord[ NUM_DIR_LIGHT_SHADOWS ];
		struct DirectionalLightShadow {
			float shadowIntensity;
			float shadowBias;
			float shadowNormalBias;
			float shadowRadius;
			vec2 shadowMapSize;
		};
		uniform DirectionalLightShadow directionalLightShadows[ NUM_DIR_LIGHT_SHADOWS ];
	#endif
	#if NUM_SPOT_LIGHT_SHADOWS > 0
		#if defined( SHADOWMAP_TYPE_PCF )
			uniform sampler2DShadow spotShadowMap[ NUM_SPOT_LIGHT_SHADOWS ];
		#else
			uniform sampler2D spotShadowMap[ NUM_SPOT_LIGHT_SHADOWS ];
		#endif
		struct SpotLightShadow {
			float shadowIntensity;
			float shadowBias;
			float shadowNormalBias;
			float shadowRadius;
			vec2 shadowMapSize;
		};
		uniform SpotLightShadow spotLightShadows[ NUM_SPOT_LIGHT_SHADOWS ];
	#endif
	#if NUM_POINT_LIGHT_SHADOWS > 0
		#if defined( SHADOWMAP_TYPE_PCF )
			uniform samplerCubeShadow pointShadowMap[ NUM_POINT_LIGHT_SHADOWS ];
		#elif defined( SHADOWMAP_TYPE_BASIC )
			uniform samplerCube pointShadowMap[ NUM_POINT_LIGHT_SHADOWS ];
		#endif
		varying vec4 vPointShadowCoord[ NUM_POINT_LIGHT_SHADOWS ];
		struct PointLightShadow {
			float shadowIntensity;
			float shadowBias;
			float shadowNormalBias;
			float shadowRadius;
			vec2 shadowMapSize;
			float shadowCameraNear;
			float shadowCameraFar;
		};
		uniform PointLightShadow pointLightShadows[ NUM_POINT_LIGHT_SHADOWS ];
	#endif
	#if defined( SHADOWMAP_TYPE_PCF )
		float interleavedGradientNoise( vec2 position ) {
			return fract( 52.9829189 * fract( dot( position, vec2( 0.06711056, 0.00583715 ) ) ) );
		}
		vec2 vogelDiskSample( int sampleIndex, int samplesCount, float phi ) {
			const float goldenAngle = 2.399963229728653;
			float r = sqrt( ( float( sampleIndex ) + 0.5 ) / float( samplesCount ) );
			float theta = float( sampleIndex ) * goldenAngle + phi;
			return vec2( cos( theta ), sin( theta ) ) * r;
		}
	#endif
	#if defined( SHADOWMAP_TYPE_PCF )
		float getShadow( sampler2DShadow shadowMap, vec2 shadowMapSize, float shadowIntensity, float shadowBias, float shadowRadius, vec4 shadowCoord ) {
			float shadow = 1.0;
			shadowCoord.xyz /= shadowCoord.w;
			shadowCoord.z += shadowBias;
			bool inFrustum = shadowCoord.x >= 0.0 && shadowCoord.x <= 1.0 && shadowCoord.y >= 0.0 && shadowCoord.y <= 1.0;
			bool frustumTest = inFrustum && shadowCoord.z <= 1.0;
			if ( frustumTest ) {
				vec2 texelSize = vec2( 1.0 ) / shadowMapSize;
				float radius = shadowRadius * texelSize.x;
				float phi = interleavedGradientNoise( gl_FragCoord.xy ) * PI2;
				shadow = (
					texture( shadowMap, vec3( shadowCoord.xy + vogelDiskSample( 0, 5, phi ) * radius, shadowCoord.z ) ) +
					texture( shadowMap, vec3( shadowCoord.xy + vogelDiskSample( 1, 5, phi ) * radius, shadowCoord.z ) ) +
					texture( shadowMap, vec3( shadowCoord.xy + vogelDiskSample( 2, 5, phi ) * radius, shadowCoord.z ) ) +
					texture( shadowMap, vec3( shadowCoord.xy + vogelDiskSample( 3, 5, phi ) * radius, shadowCoord.z ) ) +
					texture( shadowMap, vec3( shadowCoord.xy + vogelDiskSample( 4, 5, phi ) * radius, shadowCoord.z ) )
				) * 0.2;
			}
			return mix( 1.0, shadow, shadowIntensity );
		}
	#elif defined( SHADOWMAP_TYPE_VSM )
		float getShadow( sampler2D shadowMap, vec2 shadowMapSize, float shadowIntensity, float shadowBias, float shadowRadius, vec4 shadowCoord ) {
			float shadow = 1.0;
			shadowCoord.xyz /= shadowCoord.w;
			#ifdef USE_REVERSED_DEPTH_BUFFER
				shadowCoord.z -= shadowBias;
			#else
				shadowCoord.z += shadowBias;
			#endif
			bool inFrustum = shadowCoord.x >= 0.0 && shadowCoord.x <= 1.0 && shadowCoord.y >= 0.0 && shadowCoord.y <= 1.0;
			bool frustumTest = inFrustum && shadowCoord.z <= 1.0;
			if ( frustumTest ) {
				vec2 distribution = texture2D( shadowMap, shadowCoord.xy ).rg;
				float mean = distribution.x;
				float variance = distribution.y * distribution.y;
				#ifdef USE_REVERSED_DEPTH_BUFFER
					float hard_shadow = step( mean, shadowCoord.z );
				#else
					float hard_shadow = step( shadowCoord.z, mean );
				#endif
				
				if ( hard_shadow == 1.0 ) {
					shadow = 1.0;
				} else {
					variance = max( variance, 0.0000001 );
					float d = shadowCoord.z - mean;
					float p_max = variance / ( variance + d * d );
					p_max = clamp( ( p_max - 0.3 ) / 0.65, 0.0, 1.0 );
					shadow = max( hard_shadow, p_max );
				}
			}
			return mix( 1.0, shadow, shadowIntensity );
		}
	#else
		float getShadow( sampler2D shadowMap, vec2 shadowMapSize, float shadowIntensity, float shadowBias, float shadowRadius, vec4 shadowCoord ) {
			float shadow = 1.0;
			shadowCoord.xyz /= shadowCoord.w;
			#ifdef USE_REVERSED_DEPTH_BUFFER
				shadowCoord.z -= shadowBias;
			#else
				shadowCoord.z += shadowBias;
			#endif
			bool inFrustum = shadowCoord.x >= 0.0 && shadowCoord.x <= 1.0 && shadowCoord.y >= 0.0 && shadowCoord.y <= 1.0;
			bool frustumTest = inFrustum && shadowCoord.z <= 1.0;
			if ( frustumTest ) {
				float depth = texture2D( shadowMap, shadowCoord.xy ).r;
				#ifdef USE_REVERSED_DEPTH_BUFFER
					shadow = step( depth, shadowCoord.z );
				#else
					shadow = step( shadowCoord.z, depth );
				#endif
			}
			return mix( 1.0, shadow, shadowIntensity );
		}
	#endif
	#if NUM_SUN_LIGHT_SHADOWS > 0
		float getSunShadow(
			#if defined( SHADOWMAP_TYPE_PCF )
				sampler2DShadow shadowMap,
			#else
				sampler2D shadowMap,
			#endif
			SunLightShadow sunLightShadow,
			int shadowIndex
		) {
			vec4 shadowWorldPosition = vec4( vSunShadowWorldPosition.xyz + vSunShadowWorldNormal * sunLightShadow.shadowNormalBias, 1.0 );
			float viewDepth = vSunShadowWorldPosition.w;
			int cascadeOffset = shadowIndex * SUN_LIGHT_CASCADES;
			float shadow = 1.0;
			for ( int i = SUN_LIGHT_CASCADES - 1; i >= 0; i -- ) {
				vec4 cascade = sunShadowCascade[ cascadeOffset + i ];
				if ( viewDepth >= cascade.x && viewDepth < cascade.y ) {
					float cascadeShadow = getShadow(
						shadowMap,
						sunLightShadow.shadowMapSize,
						sunLightShadow.shadowIntensity,
						sunLightShadow.shadowBias,
						sunLightShadow.shadowRadius,
						sunShadowMatrix[ cascadeOffset + i ] * shadowWorldPosition
					);
					shadow = mix( cascadeShadow, shadow, smoothstep( cascade.z, cascade.y, viewDepth ) );
				}
			}
			return shadow;
		}
	#endif
	#if NUM_POINT_LIGHT_SHADOWS > 0
	#if defined( SHADOWMAP_TYPE_PCF )
	float getPointShadow( samplerCubeShadow shadowMap, vec2 shadowMapSize, float shadowIntensity, float shadowBias, float shadowRadius, vec4 shadowCoord, float shadowCameraNear, float shadowCameraFar ) {
		float shadow = 1.0;
		vec3 lightToPosition = shadowCoord.xyz;
		vec3 bd3D = normalize( lightToPosition );
		vec3 absVec = abs( lightToPosition );
		float viewSpaceZ = max( max( absVec.x, absVec.y ), absVec.z );
		if ( viewSpaceZ - shadowCameraFar <= 0.0 && viewSpaceZ - shadowCameraNear >= 0.0 ) {
			#ifdef USE_REVERSED_DEPTH_BUFFER
				float dp = ( shadowCameraNear * ( shadowCameraFar - viewSpaceZ ) ) / ( viewSpaceZ * ( shadowCameraFar - shadowCameraNear ) );
				dp -= shadowBias;
			#else
				float dp = ( shadowCameraFar * ( viewSpaceZ - shadowCameraNear ) ) / ( viewSpaceZ * ( shadowCameraFar - shadowCameraNear ) );
				dp += shadowBias;
			#endif
			float texelSize = shadowRadius / shadowMapSize.x;
			vec3 absDir = abs( bd3D );
			vec3 tangent = absDir.x > absDir.z ? vec3( 0.0, 1.0, 0.0 ) : vec3( 1.0, 0.0, 0.0 );
			tangent = normalize( cross( bd3D, tangent ) );
			vec3 bitangent = cross( bd3D, tangent );
			float phi = interleavedGradientNoise( gl_FragCoord.xy ) * PI2;
			vec2 sample0 = vogelDiskSample( 0, 5, phi );
			vec2 sample1 = vogelDiskSample( 1, 5, phi );
			vec2 sample2 = vogelDiskSample( 2, 5, phi );
			vec2 sample3 = vogelDiskSample( 3, 5, phi );
			vec2 sample4 = vogelDiskSample( 4, 5, phi );
			shadow = (
				texture( shadowMap, vec4( bd3D + ( tangent * sample0.x + bitangent * sample0.y ) * texelSize, dp ) ) +
				texture( shadowMap, vec4( bd3D + ( tangent * sample1.x + bitangent * sample1.y ) * texelSize, dp ) ) +
				texture( shadowMap, vec4( bd3D + ( tangent * sample2.x + bitangent * sample2.y ) * texelSize, dp ) ) +
				texture( shadowMap, vec4( bd3D + ( tangent * sample3.x + bitangent * sample3.y ) * texelSize, dp ) ) +
				texture( shadowMap, vec4( bd3D + ( tangent * sample4.x + bitangent * sample4.y ) * texelSize, dp ) )
			) * 0.2;
		}
		return mix( 1.0, shadow, shadowIntensity );
	}
	#elif defined( SHADOWMAP_TYPE_BASIC )
	float getPointShadow( samplerCube shadowMap, vec2 shadowMapSize, float shadowIntensity, float shadowBias, float shadowRadius, vec4 shadowCoord, float shadowCameraNear, float shadowCameraFar ) {
		float shadow = 1.0;
		vec3 lightToPosition = shadowCoord.xyz;
		vec3 absVec = abs( lightToPosition );
		float viewSpaceZ = max( max( absVec.x, absVec.y ), absVec.z );
		if ( viewSpaceZ - shadowCameraFar <= 0.0 && viewSpaceZ - shadowCameraNear >= 0.0 ) {
			float dp = ( shadowCameraFar * ( viewSpaceZ - shadowCameraNear ) ) / ( viewSpaceZ * ( shadowCameraFar - shadowCameraNear ) );
			dp += shadowBias;
			vec3 bd3D = normalize( lightToPosition );
			float depth = textureCube( shadowMap, bd3D ).r;
			#ifdef USE_REVERSED_DEPTH_BUFFER
				depth = 1.0 - depth;
			#endif
			shadow = step( dp, depth );
		}
		return mix( 1.0, shadow, shadowIntensity );
	}
	#endif
	#endif
#endif`,ux=`#if NUM_SPOT_LIGHT_COORDS > 0
	uniform mat4 spotLightMatrix[ NUM_SPOT_LIGHT_COORDS ];
	varying vec4 vSpotLightCoord[ NUM_SPOT_LIGHT_COORDS ];
#endif
#ifdef USE_SHADOWMAP
	#if NUM_SUN_LIGHT_SHADOWS > 0
		varying vec4 vSunShadowWorldPosition;
		varying vec3 vSunShadowWorldNormal;
	#endif
	#if NUM_DIR_LIGHT_SHADOWS > 0
		uniform mat4 directionalShadowMatrix[ NUM_DIR_LIGHT_SHADOWS ];
		varying vec4 vDirectionalShadowCoord[ NUM_DIR_LIGHT_SHADOWS ];
		struct DirectionalLightShadow {
			float shadowIntensity;
			float shadowBias;
			float shadowNormalBias;
			float shadowRadius;
			vec2 shadowMapSize;
		};
		uniform DirectionalLightShadow directionalLightShadows[ NUM_DIR_LIGHT_SHADOWS ];
	#endif
	#if NUM_SPOT_LIGHT_SHADOWS > 0
		struct SpotLightShadow {
			float shadowIntensity;
			float shadowBias;
			float shadowNormalBias;
			float shadowRadius;
			vec2 shadowMapSize;
		};
		uniform SpotLightShadow spotLightShadows[ NUM_SPOT_LIGHT_SHADOWS ];
	#endif
	#if NUM_POINT_LIGHT_SHADOWS > 0
		uniform mat4 pointShadowMatrix[ NUM_POINT_LIGHT_SHADOWS ];
		varying vec4 vPointShadowCoord[ NUM_POINT_LIGHT_SHADOWS ];
		struct PointLightShadow {
			float shadowIntensity;
			float shadowBias;
			float shadowNormalBias;
			float shadowRadius;
			vec2 shadowMapSize;
			float shadowCameraNear;
			float shadowCameraFar;
		};
		uniform PointLightShadow pointLightShadows[ NUM_POINT_LIGHT_SHADOWS ];
	#endif
#endif`,dx=`#if ( defined( USE_SHADOWMAP ) && ( NUM_DIR_LIGHT_SHADOWS > 0 || NUM_SUN_LIGHT_SHADOWS > 0 || NUM_POINT_LIGHT_SHADOWS > 0 ) ) || ( NUM_SPOT_LIGHT_COORDS > 0 )
	#ifdef HAS_NORMAL
		vec3 shadowWorldNormal = transformNormalByInverseViewMatrix( transformedNormal, viewMatrix );
	#else
		vec3 shadowWorldNormal = vec3( 0.0 );
	#endif
	vec4 shadowWorldPosition;
#endif
#if defined( USE_SHADOWMAP )
	#if NUM_SUN_LIGHT_SHADOWS > 0
		vSunShadowWorldPosition = vec4( worldPosition.xyz, - mvPosition.z );
		vSunShadowWorldNormal = shadowWorldNormal;
	#endif
	#if NUM_DIR_LIGHT_SHADOWS > 0
		#pragma unroll_loop_start
		for ( int i = 0; i < NUM_DIR_LIGHT_SHADOWS; i ++ ) {
			shadowWorldPosition = worldPosition + vec4( shadowWorldNormal * directionalLightShadows[ i ].shadowNormalBias, 0 );
			vDirectionalShadowCoord[ i ] = directionalShadowMatrix[ i ] * shadowWorldPosition;
		}
		#pragma unroll_loop_end
	#endif
	#if NUM_POINT_LIGHT_SHADOWS > 0
		#pragma unroll_loop_start
		for ( int i = 0; i < NUM_POINT_LIGHT_SHADOWS; i ++ ) {
			shadowWorldPosition = worldPosition + vec4( shadowWorldNormal * pointLightShadows[ i ].shadowNormalBias, 0 );
			vPointShadowCoord[ i ] = pointShadowMatrix[ i ] * shadowWorldPosition;
		}
		#pragma unroll_loop_end
	#endif
#endif
#if NUM_SPOT_LIGHT_COORDS > 0
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_SPOT_LIGHT_COORDS; i ++ ) {
		shadowWorldPosition = worldPosition;
		#if ( defined( USE_SHADOWMAP ) && UNROLLED_LOOP_INDEX < NUM_SPOT_LIGHT_SHADOWS )
			shadowWorldPosition.xyz += shadowWorldNormal * spotLightShadows[ i ].shadowNormalBias;
		#endif
		vSpotLightCoord[ i ] = spotLightMatrix[ i ] * shadowWorldPosition;
	}
	#pragma unroll_loop_end
#endif`,fx=`float getShadowMask() {
	float shadow = 1.0;
	#ifdef USE_SHADOWMAP
	#if NUM_SUN_LIGHT_SHADOWS > 0
	SunLightShadow sunLight;
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_SUN_LIGHT_SHADOWS; i ++ ) {
		sunLight = sunLightShadows[ i ];
		shadow *= receiveShadow ? getSunShadow( sunShadowMap[ i ], sunLight, UNROLLED_LOOP_INDEX ) : 1.0;
	}
	#pragma unroll_loop_end
	#endif
	#if NUM_DIR_LIGHT_SHADOWS > 0
	DirectionalLightShadow directionalLight;
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_DIR_LIGHT_SHADOWS; i ++ ) {
		directionalLight = directionalLightShadows[ i ];
		shadow *= receiveShadow ? getShadow( directionalShadowMap[ i ], directionalLight.shadowMapSize, directionalLight.shadowIntensity, directionalLight.shadowBias, directionalLight.shadowRadius, vDirectionalShadowCoord[ i ] ) : 1.0;
	}
	#pragma unroll_loop_end
	#endif
	#if NUM_SPOT_LIGHT_SHADOWS > 0
	SpotLightShadow spotLight;
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_SPOT_LIGHT_SHADOWS; i ++ ) {
		spotLight = spotLightShadows[ i ];
		shadow *= receiveShadow ? getShadow( spotShadowMap[ i ], spotLight.shadowMapSize, spotLight.shadowIntensity, spotLight.shadowBias, spotLight.shadowRadius, vSpotLightCoord[ i ] ) : 1.0;
	}
	#pragma unroll_loop_end
	#endif
	#if NUM_POINT_LIGHT_SHADOWS > 0 && ( defined( SHADOWMAP_TYPE_PCF ) || defined( SHADOWMAP_TYPE_BASIC ) )
	PointLightShadow pointLight;
	#pragma unroll_loop_start
	for ( int i = 0; i < NUM_POINT_LIGHT_SHADOWS; i ++ ) {
		pointLight = pointLightShadows[ i ];
		shadow *= receiveShadow ? getPointShadow( pointShadowMap[ i ], pointLight.shadowMapSize, pointLight.shadowIntensity, pointLight.shadowBias, pointLight.shadowRadius, vPointShadowCoord[ i ], pointLight.shadowCameraNear, pointLight.shadowCameraFar ) : 1.0;
	}
	#pragma unroll_loop_end
	#endif
	#endif
	return shadow;
}`,px=`#ifdef USE_SKINNING
	mat4 boneMatX = getBoneMatrix( skinIndex.x );
	mat4 boneMatY = getBoneMatrix( skinIndex.y );
	mat4 boneMatZ = getBoneMatrix( skinIndex.z );
	mat4 boneMatW = getBoneMatrix( skinIndex.w );
#endif`,mx=`#ifdef USE_SKINNING
	uniform mat4 bindMatrix;
	uniform mat4 bindMatrixInverse;
	uniform highp sampler2D boneTexture;
	mat4 getBoneMatrix( const in float i ) {
		int size = textureSize( boneTexture, 0 ).x;
		int j = int( i ) * 4;
		int x = j % size;
		int y = j / size;
		vec4 v1 = texelFetch( boneTexture, ivec2( x, y ), 0 );
		vec4 v2 = texelFetch( boneTexture, ivec2( x + 1, y ), 0 );
		vec4 v3 = texelFetch( boneTexture, ivec2( x + 2, y ), 0 );
		vec4 v4 = texelFetch( boneTexture, ivec2( x + 3, y ), 0 );
		return mat4( v1, v2, v3, v4 );
	}
#endif`,gx=`#ifdef USE_SKINNING
	vec4 skinVertex = bindMatrix * vec4( transformed, 1.0 );
	vec4 skinned = vec4( 0.0 );
	skinned += boneMatX * skinVertex * skinWeight.x;
	skinned += boneMatY * skinVertex * skinWeight.y;
	skinned += boneMatZ * skinVertex * skinWeight.z;
	skinned += boneMatW * skinVertex * skinWeight.w;
	transformed = ( bindMatrixInverse * skinned ).xyz;
#endif`,bx=`#ifdef USE_SKINNING
	mat4 skinMatrix = mat4( 0.0 );
	skinMatrix += skinWeight.x * boneMatX;
	skinMatrix += skinWeight.y * boneMatY;
	skinMatrix += skinWeight.z * boneMatZ;
	skinMatrix += skinWeight.w * boneMatW;
	skinMatrix = bindMatrixInverse * skinMatrix * bindMatrix;
	objectNormal = vec4( skinMatrix * vec4( objectNormal, 0.0 ) ).xyz;
	#ifdef USE_TANGENT
		objectTangent = vec4( skinMatrix * vec4( objectTangent, 0.0 ) ).xyz;
	#endif
#endif`,xx=`float specularStrength;
#ifdef USE_SPECULARMAP
	vec4 texelSpecular = texture2D( specularMap, vSpecularMapUv );
	specularStrength = texelSpecular.r;
#else
	specularStrength = 1.0;
#endif`,_x=`#ifdef USE_SPECULARMAP
	uniform sampler2D specularMap;
#endif`,yx=`#if defined( TONE_MAPPING )
	gl_FragColor.rgb = toneMapping( gl_FragColor.rgb );
#endif`,vx=`#ifndef saturate
#define saturate( a ) clamp( a, 0.0, 1.0 )
#endif
uniform float toneMappingExposure;
vec3 LinearToneMapping( vec3 color ) {
	return saturate( toneMappingExposure * color );
}
vec3 ReinhardToneMapping( vec3 color ) {
	color *= toneMappingExposure;
	return saturate( color / ( vec3( 1.0 ) + color ) );
}
vec3 CineonToneMapping( vec3 color ) {
	color *= toneMappingExposure;
	color = max( vec3( 0.0 ), color - 0.004 );
	return pow( ( color * ( 6.2 * color + 0.5 ) ) / ( color * ( 6.2 * color + 1.7 ) + 0.06 ), vec3( 2.2 ) );
}
vec3 RRTAndODTFit( vec3 v ) {
	vec3 a = v * ( v + 0.0245786 ) - 0.000090537;
	vec3 b = v * ( 0.983729 * v + 0.4329510 ) + 0.238081;
	return a / b;
}
vec3 ACESFilmicToneMapping( vec3 color ) {
	const mat3 ACESInputMat = mat3(
		vec3( 0.59719, 0.07600, 0.02840 ),		vec3( 0.35458, 0.90834, 0.13383 ),
		vec3( 0.04823, 0.01566, 0.83777 )
	);
	const mat3 ACESOutputMat = mat3(
		vec3(  1.60475, -0.10208, -0.00327 ),		vec3( -0.53108,  1.10813, -0.07276 ),
		vec3( -0.07367, -0.00605,  1.07602 )
	);
	color *= toneMappingExposure / 0.6;
	color = ACESInputMat * color;
	color = RRTAndODTFit( color );
	color = ACESOutputMat * color;
	return saturate( color );
}
const mat3 LINEAR_REC2020_TO_LINEAR_SRGB = mat3(
	vec3( 1.6605, - 0.1246, - 0.0182 ),
	vec3( - 0.5876, 1.1329, - 0.1006 ),
	vec3( - 0.0728, - 0.0083, 1.1187 )
);
const mat3 LINEAR_SRGB_TO_LINEAR_REC2020 = mat3(
	vec3( 0.6274, 0.0691, 0.0164 ),
	vec3( 0.3293, 0.9195, 0.0880 ),
	vec3( 0.0433, 0.0113, 0.8956 )
);
vec3 agxDefaultContrastApprox( vec3 x ) {
	vec3 x2 = x * x;
	vec3 x4 = x2 * x2;
	return + 15.5 * x4 * x2
		- 40.14 * x4 * x
		+ 31.96 * x4
		- 6.868 * x2 * x
		+ 0.4298 * x2
		+ 0.1191 * x
		- 0.00232;
}
vec3 AgXToneMapping( vec3 color ) {
	const mat3 AgXInsetMatrix = mat3(
		vec3( 0.856627153315983, 0.137318972929847, 0.11189821299995 ),
		vec3( 0.0951212405381588, 0.761241990602591, 0.0767994186031903 ),
		vec3( 0.0482516061458583, 0.101439036467562, 0.811302368396859 )
	);
	const mat3 AgXOutsetMatrix = mat3(
		vec3( 1.1271005818144368, - 0.1413297634984383, - 0.14132976349843826 ),
		vec3( - 0.11060664309660323, 1.157823702216272, - 0.11060664309660294 ),
		vec3( - 0.016493938717834573, - 0.016493938717834257, 1.2519364065950405 )
	);
	const float AgxMinEv = - 12.47393;	const float AgxMaxEv = 4.026069;
	color *= toneMappingExposure;
	color = LINEAR_SRGB_TO_LINEAR_REC2020 * color;
	color = AgXInsetMatrix * color;
	color = max( color, 1e-10 );	color = log2( color );
	color = ( color - AgxMinEv ) / ( AgxMaxEv - AgxMinEv );
	color = clamp( color, 0.0, 1.0 );
	color = agxDefaultContrastApprox( color );
	color = AgXOutsetMatrix * color;
	color = pow( max( vec3( 0.0 ), color ), vec3( 2.2 ) );
	color = LINEAR_REC2020_TO_LINEAR_SRGB * color;
	color = clamp( color, 0.0, 1.0 );
	return color;
}
vec3 NeutralToneMapping( vec3 color ) {
	const float StartCompression = 0.8 - 0.04;
	const float Desaturation = 0.15;
	color *= toneMappingExposure;
	float x = min( color.r, min( color.g, color.b ) );
	float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
	color -= offset;
	float peak = max( color.r, max( color.g, color.b ) );
	if ( peak < StartCompression ) return color;
	float d = 1. - StartCompression;
	float newPeak = 1. - d * d / ( peak + d - StartCompression );
	color *= newPeak / peak;
	float g = 1. - 1. / ( Desaturation * ( peak - newPeak ) + 1. );
	return mix( color, vec3( newPeak ), g );
}
vec3 CustomToneMapping( vec3 color ) { return color; }`,Mx=`#ifdef USE_TRANSMISSION
	material.transmission = transmission;
	material.transmissionAlpha = 1.0;
	material.thickness = thickness;
	material.attenuationDistance = attenuationDistance;
	material.attenuationColor = attenuationColor;
	#ifdef USE_TRANSMISSIONMAP
		material.transmission *= texture2D( transmissionMap, vTransmissionMapUv ).r;
	#endif
	#ifdef USE_THICKNESSMAP
		material.thickness *= texture2D( thicknessMap, vThicknessMapUv ).g;
	#endif
	vec3 pos = vWorldPosition;
	vec3 v = normalize( cameraPosition - pos );
	vec3 n = transformNormalByInverseViewMatrix( normal, viewMatrix );
	vec4 transmitted = getIBLVolumeRefraction(
		n, v, material.roughness, material.diffuseContribution, material.specularColorBlended, material.specularF90,
		pos, modelMatrix, viewMatrix, projectionMatrix, material.dispersion, material.ior, material.thickness,
		material.attenuationColor, material.attenuationDistance );
	material.transmissionAlpha = mix( material.transmissionAlpha, transmitted.a, material.transmission );
	totalDiffuse = mix( totalDiffuse, transmitted.rgb, material.transmission );
#endif`,Sx=`#ifdef USE_TRANSMISSION
	uniform float transmission;
	uniform float thickness;
	uniform float attenuationDistance;
	uniform vec3 attenuationColor;
	#ifdef USE_TRANSMISSIONMAP
		uniform sampler2D transmissionMap;
	#endif
	#ifdef USE_THICKNESSMAP
		uniform sampler2D thicknessMap;
	#endif
	uniform vec2 transmissionSamplerSize;
	uniform sampler2D transmissionSamplerMap;
	uniform mat4 modelMatrix;
	uniform mat4 projectionMatrix;
	varying vec3 vWorldPosition;
	float w0( float a ) {
		return ( 1.0 / 6.0 ) * ( a * ( a * ( - a + 3.0 ) - 3.0 ) + 1.0 );
	}
	float w1( float a ) {
		return ( 1.0 / 6.0 ) * ( a *  a * ( 3.0 * a - 6.0 ) + 4.0 );
	}
	float w2( float a ){
		return ( 1.0 / 6.0 ) * ( a * ( a * ( - 3.0 * a + 3.0 ) + 3.0 ) + 1.0 );
	}
	float w3( float a ) {
		return ( 1.0 / 6.0 ) * ( a * a * a );
	}
	float g0( float a ) {
		return w0( a ) + w1( a );
	}
	float g1( float a ) {
		return w2( a ) + w3( a );
	}
	float h0( float a ) {
		return - 1.0 + w1( a ) / ( w0( a ) + w1( a ) );
	}
	float h1( float a ) {
		return 1.0 + w3( a ) / ( w2( a ) + w3( a ) );
	}
	vec4 bicubic( sampler2D tex, vec2 uv, vec4 texelSize, float lod ) {
		uv = uv * texelSize.zw + 0.5;
		vec2 iuv = floor( uv );
		vec2 fuv = fract( uv );
		float g0x = g0( fuv.x );
		float g1x = g1( fuv.x );
		float h0x = h0( fuv.x );
		float h1x = h1( fuv.x );
		float h0y = h0( fuv.y );
		float h1y = h1( fuv.y );
		vec2 p0 = ( vec2( iuv.x + h0x, iuv.y + h0y ) - 0.5 ) * texelSize.xy;
		vec2 p1 = ( vec2( iuv.x + h1x, iuv.y + h0y ) - 0.5 ) * texelSize.xy;
		vec2 p2 = ( vec2( iuv.x + h0x, iuv.y + h1y ) - 0.5 ) * texelSize.xy;
		vec2 p3 = ( vec2( iuv.x + h1x, iuv.y + h1y ) - 0.5 ) * texelSize.xy;
		return g0( fuv.y ) * ( g0x * textureLod( tex, p0, lod ) + g1x * textureLod( tex, p1, lod ) ) +
			g1( fuv.y ) * ( g0x * textureLod( tex, p2, lod ) + g1x * textureLod( tex, p3, lod ) );
	}
	vec4 textureBicubic( sampler2D sampler, vec2 uv, float lod ) {
		vec2 fLodSize = vec2( textureSize( sampler, int( lod ) ) );
		vec2 cLodSize = vec2( textureSize( sampler, int( lod + 1.0 ) ) );
		vec2 fLodSizeInv = 1.0 / fLodSize;
		vec2 cLodSizeInv = 1.0 / cLodSize;
		vec4 fSample = bicubic( sampler, uv, vec4( fLodSizeInv, fLodSize ), floor( lod ) );
		vec4 cSample = bicubic( sampler, uv, vec4( cLodSizeInv, cLodSize ), ceil( lod ) );
		return mix( fSample, cSample, fract( lod ) );
	}
	vec3 getVolumeTransmissionRay( const in vec3 n, const in vec3 v, const in float thickness, const in float ior, const in mat4 modelMatrix ) {
		vec3 refractionVector = refract( - v, normalize( n ), 1.0 / ior );
		vec3 modelScale;
		modelScale.x = length( vec3( modelMatrix[ 0 ].xyz ) );
		modelScale.y = length( vec3( modelMatrix[ 1 ].xyz ) );
		modelScale.z = length( vec3( modelMatrix[ 2 ].xyz ) );
		return normalize( refractionVector ) * thickness * modelScale;
	}
	float applyIorToRoughness( const in float roughness, const in float ior ) {
		return roughness * clamp( ior * 2.0 - 2.0, 0.0, 1.0 );
	}
	vec4 getTransmissionSample( const in vec2 fragCoord, const in float roughness, const in float ior ) {
		float lod = log2( transmissionSamplerSize.x ) * applyIorToRoughness( roughness, ior );
		return textureBicubic( transmissionSamplerMap, fragCoord.xy, lod );
	}
	vec3 volumeAttenuation( const in float transmissionDistance, const in vec3 attenuationColor, const in float attenuationDistance ) {
		if ( isinf( attenuationDistance ) ) {
			return vec3( 1.0 );
		} else {
			vec3 attenuationCoefficient = -log( attenuationColor ) / attenuationDistance;
			vec3 transmittance = exp( - attenuationCoefficient * transmissionDistance );			return transmittance;
		}
	}
	vec4 getIBLVolumeRefraction( const in vec3 n, const in vec3 v, const in float roughness, const in vec3 diffuseColor,
		const in vec3 specularColor, const in float specularF90, const in vec3 position, const in mat4 modelMatrix,
		const in mat4 viewMatrix, const in mat4 projMatrix, const in float dispersion, const in float ior, const in float thickness,
		const in vec3 attenuationColor, const in float attenuationDistance ) {
		vec4 transmittedLight;
		vec3 transmittance;
		#ifdef USE_DISPERSION
			float halfSpread = ( ior - 1.0 ) * 0.025 * dispersion;
			vec3 iors = vec3( ior - halfSpread, ior, ior + halfSpread );
			for ( int i = 0; i < 3; i ++ ) {
				vec3 transmissionRay = getVolumeTransmissionRay( n, v, thickness, iors[ i ], modelMatrix );
				vec3 refractedRayExit = position + transmissionRay;
				vec4 ndcPos = projMatrix * viewMatrix * vec4( refractedRayExit, 1.0 );
				vec2 refractionCoords = ndcPos.xy / ndcPos.w;
				refractionCoords += 1.0;
				refractionCoords /= 2.0;
				vec4 transmissionSample = getTransmissionSample( refractionCoords, roughness, iors[ i ] );
				transmittedLight[ i ] = transmissionSample[ i ];
				transmittedLight.a += transmissionSample.a;
				transmittance[ i ] = diffuseColor[ i ] * volumeAttenuation( length( transmissionRay ), attenuationColor, attenuationDistance )[ i ];
			}
			transmittedLight.a /= 3.0;
		#else
			vec3 transmissionRay = getVolumeTransmissionRay( n, v, thickness, ior, modelMatrix );
			vec3 refractedRayExit = position + transmissionRay;
			vec4 ndcPos = projMatrix * viewMatrix * vec4( refractedRayExit, 1.0 );
			vec2 refractionCoords = ndcPos.xy / ndcPos.w;
			refractionCoords += 1.0;
			refractionCoords /= 2.0;
			transmittedLight = getTransmissionSample( refractionCoords, roughness, ior );
			transmittance = diffuseColor * volumeAttenuation( length( transmissionRay ), attenuationColor, attenuationDistance );
		#endif
		vec3 attenuatedColor = transmittance * transmittedLight.rgb;
		vec3 F = EnvironmentBRDF( n, v, specularColor, specularF90, roughness );
		float transmittanceFactor = ( transmittance.r + transmittance.g + transmittance.b ) / 3.0;
		return vec4( ( 1.0 - F ) * attenuatedColor, 1.0 - ( 1.0 - transmittedLight.a ) * transmittanceFactor );
	}
#endif`,Ex=`#if defined( USE_UV ) || defined( USE_ANISOTROPY )
	varying vec2 vUv;
#endif
#ifdef USE_MAP
	varying vec2 vMapUv;
#endif
#ifdef USE_ALPHAMAP
	varying vec2 vAlphaMapUv;
#endif
#ifdef USE_LIGHTMAP
	varying vec2 vLightMapUv;
#endif
#ifdef USE_AOMAP
	varying vec2 vAoMapUv;
#endif
#ifdef USE_BUMPMAP
	varying vec2 vBumpMapUv;
#endif
#ifdef USE_NORMALMAP
	varying vec2 vNormalMapUv;
#endif
#ifdef USE_EMISSIVEMAP
	varying vec2 vEmissiveMapUv;
#endif
#ifdef USE_METALNESSMAP
	varying vec2 vMetalnessMapUv;
#endif
#ifdef USE_ROUGHNESSMAP
	varying vec2 vRoughnessMapUv;
#endif
#ifdef USE_ANISOTROPYMAP
	varying vec2 vAnisotropyMapUv;
#endif
#ifdef USE_CLEARCOATMAP
	varying vec2 vClearcoatMapUv;
#endif
#ifdef USE_CLEARCOAT_NORMALMAP
	varying vec2 vClearcoatNormalMapUv;
#endif
#ifdef USE_CLEARCOAT_ROUGHNESSMAP
	varying vec2 vClearcoatRoughnessMapUv;
#endif
#ifdef USE_IRIDESCENCEMAP
	varying vec2 vIridescenceMapUv;
#endif
#ifdef USE_IRIDESCENCE_THICKNESSMAP
	varying vec2 vIridescenceThicknessMapUv;
#endif
#ifdef USE_SHEEN_COLORMAP
	varying vec2 vSheenColorMapUv;
#endif
#ifdef USE_SHEEN_ROUGHNESSMAP
	varying vec2 vSheenRoughnessMapUv;
#endif
#ifdef USE_SPECULARMAP
	varying vec2 vSpecularMapUv;
#endif
#ifdef USE_SPECULAR_COLORMAP
	varying vec2 vSpecularColorMapUv;
#endif
#ifdef USE_SPECULAR_INTENSITYMAP
	varying vec2 vSpecularIntensityMapUv;
#endif
#ifdef USE_TRANSMISSIONMAP
	uniform mat3 transmissionMapTransform;
	varying vec2 vTransmissionMapUv;
#endif
#ifdef USE_THICKNESSMAP
	uniform mat3 thicknessMapTransform;
	varying vec2 vThicknessMapUv;
#endif`,Tx=`#if defined( USE_UV ) || defined( USE_ANISOTROPY )
	varying vec2 vUv;
#endif
#ifdef USE_MAP
	uniform mat3 mapTransform;
	varying vec2 vMapUv;
#endif
#ifdef USE_ALPHAMAP
	uniform mat3 alphaMapTransform;
	varying vec2 vAlphaMapUv;
#endif
#ifdef USE_LIGHTMAP
	uniform mat3 lightMapTransform;
	varying vec2 vLightMapUv;
#endif
#ifdef USE_AOMAP
	uniform mat3 aoMapTransform;
	varying vec2 vAoMapUv;
#endif
#ifdef USE_BUMPMAP
	uniform mat3 bumpMapTransform;
	varying vec2 vBumpMapUv;
#endif
#ifdef USE_NORMALMAP
	uniform mat3 normalMapTransform;
	varying vec2 vNormalMapUv;
#endif
#ifdef USE_DISPLACEMENTMAP
	uniform mat3 displacementMapTransform;
	varying vec2 vDisplacementMapUv;
#endif
#ifdef USE_EMISSIVEMAP
	uniform mat3 emissiveMapTransform;
	varying vec2 vEmissiveMapUv;
#endif
#ifdef USE_METALNESSMAP
	uniform mat3 metalnessMapTransform;
	varying vec2 vMetalnessMapUv;
#endif
#ifdef USE_ROUGHNESSMAP
	uniform mat3 roughnessMapTransform;
	varying vec2 vRoughnessMapUv;
#endif
#ifdef USE_ANISOTROPYMAP
	uniform mat3 anisotropyMapTransform;
	varying vec2 vAnisotropyMapUv;
#endif
#ifdef USE_CLEARCOATMAP
	uniform mat3 clearcoatMapTransform;
	varying vec2 vClearcoatMapUv;
#endif
#ifdef USE_CLEARCOAT_NORMALMAP
	uniform mat3 clearcoatNormalMapTransform;
	varying vec2 vClearcoatNormalMapUv;
#endif
#ifdef USE_CLEARCOAT_ROUGHNESSMAP
	uniform mat3 clearcoatRoughnessMapTransform;
	varying vec2 vClearcoatRoughnessMapUv;
#endif
#ifdef USE_SHEEN_COLORMAP
	uniform mat3 sheenColorMapTransform;
	varying vec2 vSheenColorMapUv;
#endif
#ifdef USE_SHEEN_ROUGHNESSMAP
	uniform mat3 sheenRoughnessMapTransform;
	varying vec2 vSheenRoughnessMapUv;
#endif
#ifdef USE_IRIDESCENCEMAP
	uniform mat3 iridescenceMapTransform;
	varying vec2 vIridescenceMapUv;
#endif
#ifdef USE_IRIDESCENCE_THICKNESSMAP
	uniform mat3 iridescenceThicknessMapTransform;
	varying vec2 vIridescenceThicknessMapUv;
#endif
#ifdef USE_SPECULARMAP
	uniform mat3 specularMapTransform;
	varying vec2 vSpecularMapUv;
#endif
#ifdef USE_SPECULAR_COLORMAP
	uniform mat3 specularColorMapTransform;
	varying vec2 vSpecularColorMapUv;
#endif
#ifdef USE_SPECULAR_INTENSITYMAP
	uniform mat3 specularIntensityMapTransform;
	varying vec2 vSpecularIntensityMapUv;
#endif
#ifdef USE_TRANSMISSIONMAP
	uniform mat3 transmissionMapTransform;
	varying vec2 vTransmissionMapUv;
#endif
#ifdef USE_THICKNESSMAP
	uniform mat3 thicknessMapTransform;
	varying vec2 vThicknessMapUv;
#endif`,wx=`#if defined( USE_UV ) || defined( USE_ANISOTROPY )
	vUv = vec3( uv, 1 ).xy;
#endif
#ifdef USE_MAP
	vMapUv = ( mapTransform * vec3( MAP_UV, 1 ) ).xy;
#endif
#ifdef USE_ALPHAMAP
	vAlphaMapUv = ( alphaMapTransform * vec3( ALPHAMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_LIGHTMAP
	vLightMapUv = ( lightMapTransform * vec3( LIGHTMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_AOMAP
	vAoMapUv = ( aoMapTransform * vec3( AOMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_BUMPMAP
	vBumpMapUv = ( bumpMapTransform * vec3( BUMPMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_NORMALMAP
	vNormalMapUv = ( normalMapTransform * vec3( NORMALMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_DISPLACEMENTMAP
	vDisplacementMapUv = ( displacementMapTransform * vec3( DISPLACEMENTMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_EMISSIVEMAP
	vEmissiveMapUv = ( emissiveMapTransform * vec3( EMISSIVEMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_METALNESSMAP
	vMetalnessMapUv = ( metalnessMapTransform * vec3( METALNESSMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_ROUGHNESSMAP
	vRoughnessMapUv = ( roughnessMapTransform * vec3( ROUGHNESSMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_ANISOTROPYMAP
	vAnisotropyMapUv = ( anisotropyMapTransform * vec3( ANISOTROPYMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_CLEARCOATMAP
	vClearcoatMapUv = ( clearcoatMapTransform * vec3( CLEARCOATMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_CLEARCOAT_NORMALMAP
	vClearcoatNormalMapUv = ( clearcoatNormalMapTransform * vec3( CLEARCOAT_NORMALMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_CLEARCOAT_ROUGHNESSMAP
	vClearcoatRoughnessMapUv = ( clearcoatRoughnessMapTransform * vec3( CLEARCOAT_ROUGHNESSMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_IRIDESCENCEMAP
	vIridescenceMapUv = ( iridescenceMapTransform * vec3( IRIDESCENCEMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_IRIDESCENCE_THICKNESSMAP
	vIridescenceThicknessMapUv = ( iridescenceThicknessMapTransform * vec3( IRIDESCENCE_THICKNESSMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_SHEEN_COLORMAP
	vSheenColorMapUv = ( sheenColorMapTransform * vec3( SHEEN_COLORMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_SHEEN_ROUGHNESSMAP
	vSheenRoughnessMapUv = ( sheenRoughnessMapTransform * vec3( SHEEN_ROUGHNESSMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_SPECULARMAP
	vSpecularMapUv = ( specularMapTransform * vec3( SPECULARMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_SPECULAR_COLORMAP
	vSpecularColorMapUv = ( specularColorMapTransform * vec3( SPECULAR_COLORMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_SPECULAR_INTENSITYMAP
	vSpecularIntensityMapUv = ( specularIntensityMapTransform * vec3( SPECULAR_INTENSITYMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_TRANSMISSIONMAP
	vTransmissionMapUv = ( transmissionMapTransform * vec3( TRANSMISSIONMAP_UV, 1 ) ).xy;
#endif
#ifdef USE_THICKNESSMAP
	vThicknessMapUv = ( thicknessMapTransform * vec3( THICKNESSMAP_UV, 1 ) ).xy;
#endif`,Ax=`#if defined( USE_ENVMAP ) || defined( DISTANCE ) || defined ( USE_SHADOWMAP ) || defined ( USE_TRANSMISSION ) || NUM_SPOT_LIGHT_COORDS > 0
	vec4 worldPosition = vec4( transformed, 1.0 );
	#ifdef USE_BATCHING
		worldPosition = batchingMatrix * worldPosition;
	#endif
	#ifdef USE_INSTANCING
		worldPosition = instanceMatrix * worldPosition;
	#endif
	worldPosition = modelMatrix * worldPosition;
#endif`,Rx=`varying vec2 vUv;
uniform mat3 uvTransform;
void main() {
	vUv = ( uvTransform * vec3( uv, 1 ) ).xy;
	gl_Position = vec4( position.xy, 1.0, 1.0 );
}`,Cx=`uniform sampler2D t2D;
uniform float backgroundIntensity;
varying vec2 vUv;
void main() {
	vec4 texColor = texture2D( t2D, vUv );
	#ifdef DECODE_VIDEO_TEXTURE
		texColor = vec4( mix( pow( texColor.rgb * 0.9478672986 + vec3( 0.0521327014 ), vec3( 2.4 ) ), texColor.rgb * 0.0773993808, vec3( lessThanEqual( texColor.rgb, vec3( 0.04045 ) ) ) ), texColor.w );
	#endif
	texColor.rgb *= backgroundIntensity;
	gl_FragColor = texColor;
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
}`,Px=`varying vec3 vWorldDirection;
#include <common>
void main() {
	vWorldDirection = transformDirection( position, modelMatrix );
	#include <begin_vertex>
	#include <project_vertex>
	gl_Position.z = gl_Position.w;
}`,Ix=`#ifdef ENVMAP_TYPE_CUBE
	uniform samplerCube envMap;
#elif defined( ENVMAP_TYPE_CUBE_UV )
	uniform sampler2D envMap;
#endif
uniform float backgroundBlurriness;
uniform float backgroundIntensity;
uniform mat3 backgroundRotation;
varying vec3 vWorldDirection;
#include <cube_uv_reflection_fragment>
void main() {
	#ifdef ENVMAP_TYPE_CUBE
		vec4 texColor = textureCube( envMap, backgroundRotation * vWorldDirection );
	#elif defined( ENVMAP_TYPE_CUBE_UV )
		vec4 texColor = textureCubeUV( envMap, backgroundRotation * vWorldDirection, backgroundBlurriness );
	#else
		vec4 texColor = vec4( 0.0, 0.0, 0.0, 1.0 );
	#endif
	texColor.rgb *= backgroundIntensity;
	gl_FragColor = texColor;
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
}`,Lx=`varying vec3 vWorldDirection;
#include <common>
void main() {
	vWorldDirection = transformDirection( position, modelMatrix );
	#include <begin_vertex>
	#include <project_vertex>
	gl_Position.z = gl_Position.w;
}`,Dx=`uniform samplerCube tCube;
uniform float tFlip;
uniform float opacity;
varying vec3 vWorldDirection;
void main() {
	vec4 texColor = textureCube( tCube, vec3( tFlip * vWorldDirection.x, vWorldDirection.yz ) );
	gl_FragColor = texColor;
	gl_FragColor.a *= opacity;
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
}`,Nx=`#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <displacementmap_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
varying vec2 vHighPrecisionZW;
void main() {
	#include <uv_vertex>
	#include <batching_vertex>
	#include <skinbase_vertex>
	#include <morphinstance_vertex>
	#ifdef USE_DISPLACEMENTMAP
		#include <beginnormal_vertex>
		#include <morphnormal_vertex>
		#include <skinnormal_vertex>
	#endif
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <displacementmap_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	vHighPrecisionZW = gl_Position.zw;
}`,Fx=`#if DEPTH_PACKING == 3200
	uniform float opacity;
#endif
#include <common>
#include <packing>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
varying vec2 vHighPrecisionZW;
void main() {
	vec4 diffuseColor = vec4( 1.0 );
	#include <clipping_planes_fragment>
	#if DEPTH_PACKING == 3200
		diffuseColor.a = opacity;
	#endif
	#include <map_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	#include <logdepthbuf_fragment>
	#ifdef USE_REVERSED_DEPTH_BUFFER
		float fragCoordZ = vHighPrecisionZW[ 0 ] / vHighPrecisionZW[ 1 ];
	#else
		float fragCoordZ = 0.5 * vHighPrecisionZW[ 0 ] / vHighPrecisionZW[ 1 ] + 0.5;
	#endif
	#if DEPTH_PACKING == 3200
		gl_FragColor = vec4( vec3( 1.0 - fragCoordZ ), opacity );
	#elif DEPTH_PACKING == 3201
		gl_FragColor = packDepthToRGBA( fragCoordZ );
	#elif DEPTH_PACKING == 3202
		gl_FragColor = vec4( packDepthToRGB( fragCoordZ ), 1.0 );
	#elif DEPTH_PACKING == 3203
		gl_FragColor = vec4( packDepthToRG( fragCoordZ ), 0.0, 1.0 );
	#endif
}`,Ux=`#define DISTANCE
varying vec3 vWorldPosition;
#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <displacementmap_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	#include <batching_vertex>
	#include <skinbase_vertex>
	#include <morphinstance_vertex>
	#ifdef USE_DISPLACEMENTMAP
		#include <beginnormal_vertex>
		#include <morphnormal_vertex>
		#include <skinnormal_vertex>
	#endif
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <displacementmap_vertex>
	#include <project_vertex>
	#include <worldpos_vertex>
	#include <clipping_planes_vertex>
	vWorldPosition = worldPosition.xyz;
}`,Ox=`#define DISTANCE
uniform vec3 referencePosition;
uniform float nearDistance;
uniform float farDistance;
varying vec3 vWorldPosition;
#include <common>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( 1.0 );
	#include <clipping_planes_fragment>
	#include <map_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	float dist = length( vWorldPosition - referencePosition );
	dist = ( dist - nearDistance ) / ( farDistance - nearDistance );
	dist = saturate( dist );
	gl_FragColor = vec4( dist, 0.0, 0.0, 1.0 );
}`,Bx=`varying vec3 vWorldDirection;
#include <common>
void main() {
	vWorldDirection = transformDirection( position, modelMatrix );
	#include <begin_vertex>
	#include <project_vertex>
}`,kx=`uniform sampler2D tEquirect;
varying vec3 vWorldDirection;
#include <common>
void main() {
	vec3 direction = normalize( vWorldDirection );
	vec2 sampleUV = equirectUv( direction );
	gl_FragColor = texture2D( tEquirect, sampleUV );
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
}`,zx=`uniform float scale;
attribute float lineDistance;
varying float vLineDistance;
#include <common>
#include <uv_pars_vertex>
#include <color_pars_vertex>
#include <fog_pars_vertex>
#include <morphtarget_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	vLineDistance = scale * lineDistance;
	#include <uv_vertex>
	#include <color_vertex>
	#include <morphinstance_vertex>
	#include <morphcolor_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	#include <fog_vertex>
}`,Hx=`uniform vec3 diffuse;
uniform float opacity;
uniform float dashSize;
uniform float totalSize;
varying float vLineDistance;
#include <common>
#include <color_pars_fragment>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <fog_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	if ( mod( vLineDistance, totalSize ) > dashSize ) {
		discard;
	}
	vec3 outgoingLight = vec3( 0.0 );
	#include <logdepthbuf_fragment>
	#include <map_fragment>
	#include <color_fragment>
	outgoingLight = diffuseColor.rgb;
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
}`,Gx=`#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <envmap_pars_vertex>
#include <color_pars_vertex>
#include <fog_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	#include <color_vertex>
	#include <morphinstance_vertex>
	#include <morphcolor_vertex>
	#include <batching_vertex>
	#if defined ( USE_ENVMAP ) || defined ( USE_SKINNING )
		#include <beginnormal_vertex>
		#include <morphnormal_vertex>
		#include <skinbase_vertex>
		#include <skinnormal_vertex>
		#include <defaultnormal_vertex>
	#endif
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	#include <worldpos_vertex>
	#include <envmap_vertex>
	#include <fog_vertex>
}`,Vx=`uniform vec3 diffuse;
uniform float opacity;
#ifndef FLAT_SHADED
	varying vec3 vNormal;
#endif
#include <common>
#include <dithering_pars_fragment>
#include <color_pars_fragment>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <aomap_pars_fragment>
#include <lightmap_pars_fragment>
#include <envmap_common_pars_fragment>
#include <envmap_pars_fragment>
#include <fog_pars_fragment>
#include <specularmap_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	#include <logdepthbuf_fragment>
	#include <map_fragment>
	#include <color_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	#include <specularmap_fragment>
	ReflectedLight reflectedLight = ReflectedLight( vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ) );
	#ifdef USE_LIGHTMAP
		vec4 lightMapTexel = texture2D( lightMap, vLightMapUv );
		reflectedLight.indirectDiffuse += lightMapTexel.rgb * lightMapIntensity * RECIPROCAL_PI;
	#else
		reflectedLight.indirectDiffuse += vec3( 1.0 );
	#endif
	#include <aomap_fragment>
	reflectedLight.indirectDiffuse *= diffuseColor.rgb;
	vec3 outgoingLight = reflectedLight.indirectDiffuse;
	#include <envmap_fragment>
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
	#include <dithering_fragment>
}`,Wx=`#define LAMBERT
varying vec3 vViewPosition;
#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <displacementmap_pars_vertex>
#include <envmap_pars_vertex>
#include <color_pars_vertex>
#include <fog_pars_vertex>
#include <normal_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <shadowmap_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	#include <color_vertex>
	#include <morphinstance_vertex>
	#include <morphcolor_vertex>
	#include <batching_vertex>
	#include <beginnormal_vertex>
	#include <morphnormal_vertex>
	#include <skinbase_vertex>
	#include <skinnormal_vertex>
	#include <defaultnormal_vertex>
	#include <normal_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <displacementmap_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	vViewPosition = - mvPosition.xyz;
	#include <worldpos_vertex>
	#include <envmap_vertex>
	#include <shadowmap_vertex>
	#include <fog_vertex>
}`,qx=`#define LAMBERT
uniform vec3 diffuse;
uniform vec3 emissive;
uniform float opacity;
#include <common>
#include <dithering_pars_fragment>
#include <color_pars_fragment>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <aomap_pars_fragment>
#include <lightmap_pars_fragment>
#include <emissivemap_pars_fragment>
#include <cube_uv_reflection_fragment>
#include <envmap_common_pars_fragment>
#include <envmap_pars_fragment>
#include <envmap_physical_pars_fragment>
#include <fog_pars_fragment>
#include <bsdfs>
#include <lights_pars_begin>
#include <normal_pars_fragment>
#include <lights_lambert_pars_fragment>
#include <shadowmap_pars_fragment>
#include <bumpmap_pars_fragment>
#include <normalmap_pars_fragment>
#include <specularmap_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	ReflectedLight reflectedLight = ReflectedLight( vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ) );
	vec3 totalEmissiveRadiance = emissive;
	#include <logdepthbuf_fragment>
	#include <map_fragment>
	#include <color_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	#include <specularmap_fragment>
	#include <normal_fragment_begin>
	#include <normal_fragment_maps>
	#include <emissivemap_fragment>
	#include <lights_lambert_fragment>
	#include <lights_fragment_begin>
	#include <lights_fragment_maps>
	#include <lights_fragment_end>
	#include <aomap_fragment>
	vec3 outgoingLight = reflectedLight.directDiffuse + reflectedLight.indirectDiffuse + totalEmissiveRadiance;
	#include <envmap_fragment>
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
	#include <dithering_fragment>
}`,Xx=`#define MATCAP
varying vec3 vViewPosition;
#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <color_pars_vertex>
#include <displacementmap_pars_vertex>
#include <fog_pars_vertex>
#include <normal_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	#include <color_vertex>
	#include <morphinstance_vertex>
	#include <morphcolor_vertex>
	#include <batching_vertex>
	#include <beginnormal_vertex>
	#include <morphnormal_vertex>
	#include <skinbase_vertex>
	#include <skinnormal_vertex>
	#include <defaultnormal_vertex>
	#include <normal_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <displacementmap_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	#include <fog_vertex>
	vViewPosition = - mvPosition.xyz;
}`,jx=`#define MATCAP
uniform vec3 diffuse;
uniform float opacity;
uniform sampler2D matcap;
varying vec3 vViewPosition;
#include <common>
#include <dithering_pars_fragment>
#include <color_pars_fragment>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <fog_pars_fragment>
#include <normal_pars_fragment>
#include <bumpmap_pars_fragment>
#include <normalmap_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	#include <logdepthbuf_fragment>
	#include <map_fragment>
	#include <color_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	#include <normal_fragment_begin>
	#include <normal_fragment_maps>
	vec3 viewDir = normalize( vViewPosition );
	vec3 x = normalize( vec3( viewDir.z, 0.0, - viewDir.x ) );
	vec3 y = cross( viewDir, x );
	vec2 uv = vec2( dot( x, normal ), dot( y, normal ) ) * 0.495 + 0.5;
	#ifdef USE_MATCAP
		vec4 matcapColor = texture2D( matcap, uv );
	#else
		vec4 matcapColor = vec4( vec3( mix( 0.2, 0.8, uv.y ) ), 1.0 );
	#endif
	vec3 outgoingLight = diffuseColor.rgb * matcapColor.rgb;
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
	#include <dithering_fragment>
}`,Kx=`#define NORMAL
#if defined( FLAT_SHADED ) || defined( USE_BUMPMAP ) || defined( USE_NORMALMAP_TANGENTSPACE )
	varying vec3 vViewPosition;
#endif
#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <displacementmap_pars_vertex>
#include <normal_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	#include <batching_vertex>
	#include <beginnormal_vertex>
	#include <morphinstance_vertex>
	#include <morphnormal_vertex>
	#include <skinbase_vertex>
	#include <skinnormal_vertex>
	#include <defaultnormal_vertex>
	#include <normal_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <displacementmap_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
#if defined( FLAT_SHADED ) || defined( USE_BUMPMAP ) || defined( USE_NORMALMAP_TANGENTSPACE )
	vViewPosition = - mvPosition.xyz;
#endif
}`,Yx=`#define NORMAL
uniform float opacity;
#if defined( FLAT_SHADED ) || defined( USE_BUMPMAP ) || defined( USE_NORMALMAP_TANGENTSPACE )
	varying vec3 vViewPosition;
#endif
#include <uv_pars_fragment>
#include <normal_pars_fragment>
#include <bumpmap_pars_fragment>
#include <normalmap_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( 0.0, 0.0, 0.0, opacity );
	#include <clipping_planes_fragment>
	#include <logdepthbuf_fragment>
	#include <normal_fragment_begin>
	#include <normal_fragment_maps>
	gl_FragColor = vec4( normalize( normal ) * 0.5 + 0.5, diffuseColor.a );
	#ifdef OPAQUE
		gl_FragColor.a = 1.0;
	#endif
}`,Jx=`#define PHONG
varying vec3 vViewPosition;
#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <displacementmap_pars_vertex>
#include <envmap_pars_vertex>
#include <color_pars_vertex>
#include <fog_pars_vertex>
#include <normal_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <shadowmap_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	#include <color_vertex>
	#include <morphcolor_vertex>
	#include <batching_vertex>
	#include <beginnormal_vertex>
	#include <morphinstance_vertex>
	#include <morphnormal_vertex>
	#include <skinbase_vertex>
	#include <skinnormal_vertex>
	#include <defaultnormal_vertex>
	#include <normal_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <displacementmap_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	vViewPosition = - mvPosition.xyz;
	#include <worldpos_vertex>
	#include <envmap_vertex>
	#include <shadowmap_vertex>
	#include <fog_vertex>
}`,Zx=`#define PHONG
uniform vec3 diffuse;
uniform vec3 emissive;
uniform vec3 specular;
uniform float shininess;
uniform float opacity;
#include <common>
#include <dithering_pars_fragment>
#include <color_pars_fragment>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <aomap_pars_fragment>
#include <lightmap_pars_fragment>
#include <emissivemap_pars_fragment>
#include <cube_uv_reflection_fragment>
#include <envmap_common_pars_fragment>
#include <envmap_pars_fragment>
#include <envmap_physical_pars_fragment>
#include <fog_pars_fragment>
#include <bsdfs>
#include <lights_pars_begin>
#include <normal_pars_fragment>
#include <lights_phong_pars_fragment>
#include <shadowmap_pars_fragment>
#include <bumpmap_pars_fragment>
#include <normalmap_pars_fragment>
#include <specularmap_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	ReflectedLight reflectedLight = ReflectedLight( vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ) );
	vec3 totalEmissiveRadiance = emissive;
	#include <logdepthbuf_fragment>
	#include <map_fragment>
	#include <color_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	#include <specularmap_fragment>
	#include <normal_fragment_begin>
	#include <normal_fragment_maps>
	#include <emissivemap_fragment>
	#include <lights_phong_fragment>
	#include <lights_fragment_begin>
	#include <lights_fragment_maps>
	#include <lights_fragment_end>
	#include <aomap_fragment>
	vec3 outgoingLight = reflectedLight.directDiffuse + reflectedLight.indirectDiffuse + reflectedLight.directSpecular + reflectedLight.indirectSpecular + totalEmissiveRadiance;
	#include <envmap_fragment>
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
	#include <dithering_fragment>
}`,$x=`#define STANDARD
varying vec3 vViewPosition;
#ifdef USE_TRANSMISSION
	varying vec3 vWorldPosition;
#endif
#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <displacementmap_pars_vertex>
#include <color_pars_vertex>
#include <fog_pars_vertex>
#include <normal_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <shadowmap_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	#include <color_vertex>
	#include <morphinstance_vertex>
	#include <morphcolor_vertex>
	#include <batching_vertex>
	#include <beginnormal_vertex>
	#include <morphnormal_vertex>
	#include <skinbase_vertex>
	#include <skinnormal_vertex>
	#include <defaultnormal_vertex>
	#include <normal_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <displacementmap_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	vViewPosition = - mvPosition.xyz;
	#include <worldpos_vertex>
	#include <shadowmap_vertex>
	#include <fog_vertex>
#ifdef USE_TRANSMISSION
	vWorldPosition = worldPosition.xyz;
#endif
}`,Qx=`#define STANDARD
#ifdef PHYSICAL
	#define IOR
	#define USE_SPECULAR
#endif
uniform vec3 diffuse;
uniform vec3 emissive;
uniform float roughness;
uniform float metalness;
uniform float opacity;
#ifdef IOR
	uniform float ior;
#endif
#ifdef USE_SPECULAR
	uniform float specularIntensity;
	uniform vec3 specularColor;
	#ifdef USE_SPECULAR_COLORMAP
		uniform sampler2D specularColorMap;
	#endif
	#ifdef USE_SPECULAR_INTENSITYMAP
		uniform sampler2D specularIntensityMap;
	#endif
#endif
#ifdef USE_CLEARCOAT
	uniform float clearcoat;
	uniform float clearcoatRoughness;
#endif
#ifdef USE_DISPERSION
	uniform float dispersion;
#endif
#ifdef USE_RETROREFLECTION
	uniform float retroreflectivity;
#endif
#ifdef USE_IRIDESCENCE
	uniform float iridescence;
	uniform float iridescenceIOR;
	uniform float iridescenceThicknessMinimum;
	uniform float iridescenceThicknessMaximum;
#endif
#ifdef USE_SHEEN
	uniform vec3 sheenColor;
	uniform float sheenRoughness;
	#ifdef USE_SHEEN_COLORMAP
		uniform sampler2D sheenColorMap;
	#endif
	#ifdef USE_SHEEN_ROUGHNESSMAP
		uniform sampler2D sheenRoughnessMap;
	#endif
#endif
#ifdef USE_ANISOTROPY
	uniform vec2 anisotropyVector;
	#ifdef USE_ANISOTROPYMAP
		uniform sampler2D anisotropyMap;
	#endif
#endif
varying vec3 vViewPosition;
#include <common>
#include <dithering_pars_fragment>
#include <color_pars_fragment>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <aomap_pars_fragment>
#include <lightmap_pars_fragment>
#include <emissivemap_pars_fragment>
#include <iridescence_fragment>
#include <cube_uv_reflection_fragment>
#include <envmap_common_pars_fragment>
#include <envmap_physical_pars_fragment>
#include <fog_pars_fragment>
#include <lights_pars_begin>
#include <normal_pars_fragment>
#include <lights_physical_pars_fragment>
#include <transmission_pars_fragment>
#include <shadowmap_pars_fragment>
#include <bumpmap_pars_fragment>
#include <normalmap_pars_fragment>
#include <clearcoat_pars_fragment>
#include <iridescence_pars_fragment>
#include <roughnessmap_pars_fragment>
#include <metalnessmap_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	ReflectedLight reflectedLight = ReflectedLight( vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ) );
	vec3 totalEmissiveRadiance = emissive;
	#include <logdepthbuf_fragment>
	#include <map_fragment>
	#include <color_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	#include <roughnessmap_fragment>
	#include <metalnessmap_fragment>
	#include <normal_fragment_begin>
	#include <normal_fragment_maps>
	#include <clearcoat_normal_fragment_begin>
	#include <clearcoat_normal_fragment_maps>
	#include <emissivemap_fragment>
	#include <lights_physical_fragment>
	#include <lights_fragment_begin>
	#include <lights_fragment_maps>
	#include <lights_fragment_end>
	#include <aomap_fragment>
	vec3 totalDiffuse = reflectedLight.directDiffuse + reflectedLight.indirectDiffuse;
	vec3 totalSpecular = reflectedLight.directSpecular + reflectedLight.indirectSpecular;
	#include <transmission_fragment>
	vec3 outgoingLight = totalDiffuse + totalSpecular + totalEmissiveRadiance;
	#ifdef USE_SHEEN
 
		outgoingLight = outgoingLight + sheenSpecularDirect + sheenSpecularIndirect;
 
 	#endif
	#ifdef USE_CLEARCOAT
		float dotNVcc = saturate( dot( geometryClearcoatNormal, geometryViewDir ) );
		vec3 Fcc = F_Schlick( material.clearcoatF0, material.clearcoatF90, dotNVcc );
		outgoingLight = outgoingLight * ( 1.0 - material.clearcoat * Fcc ) + ( clearcoatSpecularDirect + clearcoatSpecularIndirect ) * material.clearcoat;
	#endif
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
	#include <dithering_fragment>
}`,e_=`#define TOON
varying vec3 vViewPosition;
#include <common>
#include <batching_pars_vertex>
#include <uv_pars_vertex>
#include <displacementmap_pars_vertex>
#include <color_pars_vertex>
#include <fog_pars_vertex>
#include <normal_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <shadowmap_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	#include <color_vertex>
	#include <morphinstance_vertex>
	#include <morphcolor_vertex>
	#include <batching_vertex>
	#include <beginnormal_vertex>
	#include <morphnormal_vertex>
	#include <skinbase_vertex>
	#include <skinnormal_vertex>
	#include <defaultnormal_vertex>
	#include <normal_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <displacementmap_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	vViewPosition = - mvPosition.xyz;
	#include <worldpos_vertex>
	#include <shadowmap_vertex>
	#include <fog_vertex>
}`,t_=`#define TOON
uniform vec3 diffuse;
uniform vec3 emissive;
uniform float opacity;
#include <common>
#include <dithering_pars_fragment>
#include <color_pars_fragment>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <aomap_pars_fragment>
#include <lightmap_pars_fragment>
#include <emissivemap_pars_fragment>
#include <gradientmap_pars_fragment>
#include <fog_pars_fragment>
#include <bsdfs>
#include <lights_pars_begin>
#include <normal_pars_fragment>
#include <lights_toon_pars_fragment>
#include <shadowmap_pars_fragment>
#include <bumpmap_pars_fragment>
#include <normalmap_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	ReflectedLight reflectedLight = ReflectedLight( vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ), vec3( 0.0 ) );
	vec3 totalEmissiveRadiance = emissive;
	#include <logdepthbuf_fragment>
	#include <map_fragment>
	#include <color_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	#include <normal_fragment_begin>
	#include <normal_fragment_maps>
	#include <emissivemap_fragment>
	#include <lights_toon_fragment>
	#include <lights_fragment_begin>
	#include <lights_fragment_maps>
	#include <lights_fragment_end>
	#include <aomap_fragment>
	vec3 outgoingLight = reflectedLight.directDiffuse + reflectedLight.indirectDiffuse + totalEmissiveRadiance;
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
	#include <dithering_fragment>
}`,n_=`uniform float size;
uniform float scale;
#include <common>
#include <color_pars_vertex>
#include <fog_pars_vertex>
#include <morphtarget_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
#ifdef USE_POINTS_UV
	varying vec2 vUv;
	uniform mat3 uvTransform;
#endif
void main() {
	#ifdef USE_POINTS_UV
		vUv = ( uvTransform * vec3( uv, 1 ) ).xy;
	#endif
	#include <color_vertex>
	#include <morphinstance_vertex>
	#include <morphcolor_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <project_vertex>
	gl_PointSize = size;
	#ifdef USE_SIZEATTENUATION
		bool isPerspective = isPerspectiveMatrix( projectionMatrix );
		if ( isPerspective ) gl_PointSize *= ( scale / - mvPosition.z );
	#endif
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	#include <worldpos_vertex>
	#include <fog_vertex>
}`,i_=`uniform vec3 diffuse;
uniform float opacity;
#include <common>
#include <color_pars_fragment>
#include <map_particle_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <fog_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	vec3 outgoingLight = vec3( 0.0 );
	#include <logdepthbuf_fragment>
	#include <map_particle_fragment>
	#include <color_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	outgoingLight = diffuseColor.rgb;
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
}`,s_=`#include <common>
#include <batching_pars_vertex>
#include <fog_pars_vertex>
#include <morphtarget_pars_vertex>
#include <skinning_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <shadowmap_pars_vertex>
void main() {
	#include <batching_vertex>
	#include <beginnormal_vertex>
	#include <morphinstance_vertex>
	#include <morphnormal_vertex>
	#include <skinbase_vertex>
	#include <skinnormal_vertex>
	#include <defaultnormal_vertex>
	#include <begin_vertex>
	#include <morphtarget_vertex>
	#include <skinning_vertex>
	#include <project_vertex>
	#include <logdepthbuf_vertex>
	#include <worldpos_vertex>
	#include <shadowmap_vertex>
	#include <fog_vertex>
}`,r_=`uniform vec3 color;
uniform float opacity;
#include <common>
#include <fog_pars_fragment>
#include <bsdfs>
#include <lights_pars_begin>
#include <logdepthbuf_pars_fragment>
#include <shadowmap_pars_fragment>
#include <shadowmask_pars_fragment>
void main() {
	#include <logdepthbuf_fragment>
	gl_FragColor = vec4( color, opacity * ( 1.0 - getShadowMask() ) );
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
	#include <premultiplied_alpha_fragment>
}`,a_=`uniform float rotation;
uniform vec2 center;
#include <common>
#include <uv_pars_vertex>
#include <fog_pars_vertex>
#include <logdepthbuf_pars_vertex>
#include <clipping_planes_pars_vertex>
void main() {
	#include <uv_vertex>
	vec4 mvPosition = modelViewMatrix[ 3 ];
	vec2 scale = vec2( length( modelMatrix[ 0 ].xyz ), length( modelMatrix[ 1 ].xyz ) );
	#ifndef USE_SIZEATTENUATION
		bool isPerspective = isPerspectiveMatrix( projectionMatrix );
		if ( isPerspective ) scale *= - mvPosition.z;
	#endif
	vec2 alignedPosition = ( position.xy - ( center - vec2( 0.5 ) ) ) * scale;
	vec2 rotatedPosition;
	rotatedPosition.x = cos( rotation ) * alignedPosition.x - sin( rotation ) * alignedPosition.y;
	rotatedPosition.y = sin( rotation ) * alignedPosition.x + cos( rotation ) * alignedPosition.y;
	mvPosition.xy += rotatedPosition;
	gl_Position = projectionMatrix * mvPosition;
	#include <logdepthbuf_vertex>
	#include <clipping_planes_vertex>
	#include <fog_vertex>
}`,o_=`uniform vec3 diffuse;
uniform float opacity;
#include <common>
#include <uv_pars_fragment>
#include <map_pars_fragment>
#include <alphamap_pars_fragment>
#include <alphatest_pars_fragment>
#include <alphahash_pars_fragment>
#include <fog_pars_fragment>
#include <logdepthbuf_pars_fragment>
#include <clipping_planes_pars_fragment>
void main() {
	vec4 diffuseColor = vec4( diffuse, opacity );
	#include <clipping_planes_fragment>
	vec3 outgoingLight = vec3( 0.0 );
	#include <logdepthbuf_fragment>
	#include <map_fragment>
	#include <alphamap_fragment>
	#include <alphatest_fragment>
	#include <alphahash_fragment>
	outgoingLight = diffuseColor.rgb;
	#include <opaque_fragment>
	#include <tonemapping_fragment>
	#include <colorspace_fragment>
	#include <fog_fragment>
}`,ot={alphahash_fragment:Rg,alphahash_pars_fragment:Cg,alphamap_fragment:Pg,alphamap_pars_fragment:Ig,alphatest_fragment:Lg,alphatest_pars_fragment:Dg,aomap_fragment:Ng,aomap_pars_fragment:Fg,batching_pars_vertex:Ug,batching_vertex:Og,begin_vertex:Bg,beginnormal_vertex:kg,bsdfs:zg,iridescence_fragment:Hg,bumpmap_pars_fragment:Gg,clipping_planes_fragment:Vg,clipping_planes_pars_fragment:Wg,clipping_planes_pars_vertex:qg,clipping_planes_vertex:Xg,color_fragment:jg,color_pars_fragment:Kg,color_pars_vertex:Yg,color_vertex:Jg,common:Zg,cube_uv_reflection_fragment:$g,defaultnormal_vertex:Qg,displacementmap_pars_vertex:eb,displacementmap_vertex:tb,emissivemap_fragment:nb,emissivemap_pars_fragment:ib,colorspace_fragment:sb,colorspace_pars_fragment:rb,envmap_fragment:ab,envmap_common_pars_fragment:ob,envmap_pars_fragment:cb,envmap_pars_vertex:lb,envmap_physical_pars_fragment:yb,envmap_vertex:hb,fog_vertex:ub,fog_pars_vertex:db,fog_fragment:fb,fog_pars_fragment:pb,gradientmap_pars_fragment:mb,lightmap_pars_fragment:gb,lights_lambert_fragment:bb,lights_lambert_pars_fragment:xb,lights_pars_begin:_b,lights_toon_fragment:vb,lights_toon_pars_fragment:Mb,lights_phong_fragment:Sb,lights_phong_pars_fragment:Eb,lights_physical_fragment:Tb,lights_physical_pars_fragment:wb,lights_fragment_begin:Ab,lights_fragment_maps:Rb,lights_fragment_end:Cb,lightprobes_pars_fragment:Pb,logdepthbuf_fragment:Ib,logdepthbuf_pars_fragment:Lb,logdepthbuf_pars_vertex:Db,logdepthbuf_vertex:Nb,map_fragment:Fb,map_pars_fragment:Ub,map_particle_fragment:Ob,map_particle_pars_fragment:Bb,metalnessmap_fragment:kb,metalnessmap_pars_fragment:zb,morphinstance_vertex:Hb,morphcolor_vertex:Gb,morphnormal_vertex:Vb,morphtarget_pars_vertex:Wb,morphtarget_vertex:qb,normal_fragment_begin:Xb,normal_fragment_maps:jb,normal_pars_fragment:Kb,normal_pars_vertex:Yb,normal_vertex:Jb,normalmap_pars_fragment:Zb,clearcoat_normal_fragment_begin:$b,clearcoat_normal_fragment_maps:Qb,clearcoat_pars_fragment:ex,iridescence_pars_fragment:tx,opaque_fragment:nx,packing:ix,premultiplied_alpha_fragment:sx,project_vertex:rx,dithering_fragment:ax,dithering_pars_fragment:ox,roughnessmap_fragment:cx,roughnessmap_pars_fragment:lx,shadowmap_pars_fragment:hx,shadowmap_pars_vertex:ux,shadowmap_vertex:dx,shadowmask_pars_fragment:fx,skinbase_vertex:px,skinning_pars_vertex:mx,skinning_vertex:gx,skinnormal_vertex:bx,specularmap_fragment:xx,specularmap_pars_fragment:_x,tonemapping_fragment:yx,tonemapping_pars_fragment:vx,transmission_fragment:Mx,transmission_pars_fragment:Sx,uv_pars_fragment:Ex,uv_pars_vertex:Tx,uv_vertex:wx,worldpos_vertex:Ax,background_vert:Rx,background_frag:Cx,backgroundCube_vert:Px,backgroundCube_frag:Ix,cube_vert:Lx,cube_frag:Dx,depth_vert:Nx,depth_frag:Fx,distance_vert:Ux,distance_frag:Ox,equirect_vert:Bx,equirect_frag:kx,linedashed_vert:zx,linedashed_frag:Hx,meshbasic_vert:Gx,meshbasic_frag:Vx,meshlambert_vert:Wx,meshlambert_frag:qx,meshmatcap_vert:Xx,meshmatcap_frag:jx,meshnormal_vert:Kx,meshnormal_frag:Yx,meshphong_vert:Jx,meshphong_frag:Zx,meshphysical_vert:$x,meshphysical_frag:Qx,meshtoon_vert:e_,meshtoon_frag:t_,points_vert:n_,points_frag:i_,shadow_vert:s_,shadow_frag:r_,sprite_vert:a_,sprite_frag:o_},Ee={common:{diffuse:{value:new Be(16777215)},opacity:{value:1},map:{value:null},mapTransform:{value:new et},alphaMap:{value:null},alphaMapTransform:{value:new et},alphaTest:{value:0}},specularmap:{specularMap:{value:null},specularMapTransform:{value:new et}},envmap:{envMap:{value:null},envMapRotation:{value:new et},reflectivity:{value:1},ior:{value:1.5},refractionRatio:{value:.98},dfgLUT:{value:null}},aomap:{aoMap:{value:null},aoMapIntensity:{value:1},aoMapTransform:{value:new et}},lightmap:{lightMap:{value:null},lightMapIntensity:{value:1},lightMapTransform:{value:new et}},bumpmap:{bumpMap:{value:null},bumpMapTransform:{value:new et},bumpScale:{value:1}},normalmap:{normalMap:{value:null},normalMapTransform:{value:new et},normalScale:{value:new ae(1,1)}},displacementmap:{displacementMap:{value:null},displacementMapTransform:{value:new et},displacementScale:{value:1},displacementBias:{value:0}},emissivemap:{emissiveMap:{value:null},emissiveMapTransform:{value:new et}},metalnessmap:{metalnessMap:{value:null},metalnessMapTransform:{value:new et}},roughnessmap:{roughnessMap:{value:null},roughnessMapTransform:{value:new et}},gradientmap:{gradientMap:{value:null}},fog:{fogDensity:{value:25e-5},fogNear:{value:1},fogFar:{value:2e3},fogColor:{value:new Be(16777215)}},lights:{ambientLightColor:{value:[]},lightProbe:{value:[]},sunLights:{value:[],properties:{direction:{},color:{}}},sunLightShadows:{value:[],properties:{shadowIntensity:1,shadowBias:{},shadowNormalBias:{},shadowRadius:{},shadowMapSize:{}}},sunShadowMatrix:{value:[]},sunShadowCascade:{value:[]},directionalLights:{value:[],properties:{direction:{},color:{}}},directionalLightShadows:{value:[],properties:{shadowIntensity:1,shadowBias:{},shadowNormalBias:{},shadowRadius:{},shadowMapSize:{}}},directionalShadowMatrix:{value:[]},spotLights:{value:[],properties:{color:{},position:{},direction:{},distance:{},coneCos:{},penumbraCos:{},decay:{}}},spotLightShadows:{value:[],properties:{shadowIntensity:1,shadowBias:{},shadowNormalBias:{},shadowRadius:{},shadowMapSize:{}}},spotLightMap:{value:[]},spotLightMatrix:{value:[]},pointLights:{value:[],properties:{color:{},position:{},decay:{},distance:{}}},pointLightShadows:{value:[],properties:{shadowIntensity:1,shadowBias:{},shadowNormalBias:{},shadowRadius:{},shadowMapSize:{},shadowCameraNear:{},shadowCameraFar:{}}},pointShadowMatrix:{value:[]},hemisphereLights:{value:[],properties:{direction:{},skyColor:{},groundColor:{}}},rectAreaLights:{value:[],properties:{color:{},position:{},width:{},height:{}}},ltc_1:{value:null},ltc_2:{value:null},probesSH:{value:null},probesMin:{value:new P},probesMax:{value:new P},probesResolution:{value:new P}},points:{diffuse:{value:new Be(16777215)},opacity:{value:1},size:{value:1},scale:{value:1},map:{value:null},alphaMap:{value:null},alphaMapTransform:{value:new et},alphaTest:{value:0},uvTransform:{value:new et}},sprite:{diffuse:{value:new Be(16777215)},opacity:{value:1},center:{value:new ae(.5,.5)},rotation:{value:0},map:{value:null},mapTransform:{value:new et},alphaMap:{value:null},alphaMapTransform:{value:new et},alphaTest:{value:0}}},ki={basic:{uniforms:An([Ee.common,Ee.specularmap,Ee.envmap,Ee.aomap,Ee.lightmap,Ee.fog]),vertexShader:ot.meshbasic_vert,fragmentShader:ot.meshbasic_frag},lambert:{uniforms:An([Ee.common,Ee.specularmap,Ee.envmap,Ee.aomap,Ee.lightmap,Ee.emissivemap,Ee.bumpmap,Ee.normalmap,Ee.displacementmap,Ee.fog,Ee.lights,{emissive:{value:new Be(0)},envMapIntensity:{value:1}}]),vertexShader:ot.meshlambert_vert,fragmentShader:ot.meshlambert_frag},phong:{uniforms:An([Ee.common,Ee.specularmap,Ee.envmap,Ee.aomap,Ee.lightmap,Ee.emissivemap,Ee.bumpmap,Ee.normalmap,Ee.displacementmap,Ee.fog,Ee.lights,{emissive:{value:new Be(0)},specular:{value:new Be(1118481)},shininess:{value:30},envMapIntensity:{value:1}}]),vertexShader:ot.meshphong_vert,fragmentShader:ot.meshphong_frag},standard:{uniforms:An([Ee.common,Ee.envmap,Ee.aomap,Ee.lightmap,Ee.emissivemap,Ee.bumpmap,Ee.normalmap,Ee.displacementmap,Ee.roughnessmap,Ee.metalnessmap,Ee.fog,Ee.lights,{emissive:{value:new Be(0)},roughness:{value:1},metalness:{value:0},envMapIntensity:{value:1}}]),vertexShader:ot.meshphysical_vert,fragmentShader:ot.meshphysical_frag},toon:{uniforms:An([Ee.common,Ee.aomap,Ee.lightmap,Ee.emissivemap,Ee.bumpmap,Ee.normalmap,Ee.displacementmap,Ee.gradientmap,Ee.fog,Ee.lights,{emissive:{value:new Be(0)}}]),vertexShader:ot.meshtoon_vert,fragmentShader:ot.meshtoon_frag},matcap:{uniforms:An([Ee.common,Ee.bumpmap,Ee.normalmap,Ee.displacementmap,Ee.fog,{matcap:{value:null}}]),vertexShader:ot.meshmatcap_vert,fragmentShader:ot.meshmatcap_frag},points:{uniforms:An([Ee.points,Ee.fog]),vertexShader:ot.points_vert,fragmentShader:ot.points_frag},dashed:{uniforms:An([Ee.common,Ee.fog,{scale:{value:1},dashSize:{value:1},totalSize:{value:2}}]),vertexShader:ot.linedashed_vert,fragmentShader:ot.linedashed_frag},depth:{uniforms:An([Ee.common,Ee.displacementmap]),vertexShader:ot.depth_vert,fragmentShader:ot.depth_frag},normal:{uniforms:An([Ee.common,Ee.bumpmap,Ee.normalmap,Ee.displacementmap,{opacity:{value:1}}]),vertexShader:ot.meshnormal_vert,fragmentShader:ot.meshnormal_frag},sprite:{uniforms:An([Ee.sprite,Ee.fog]),vertexShader:ot.sprite_vert,fragmentShader:ot.sprite_frag},background:{uniforms:{uvTransform:{value:new et},t2D:{value:null},backgroundIntensity:{value:1}},vertexShader:ot.background_vert,fragmentShader:ot.background_frag},backgroundCube:{uniforms:{envMap:{value:null},backgroundBlurriness:{value:0},backgroundIntensity:{value:1},backgroundRotation:{value:new et}},vertexShader:ot.backgroundCube_vert,fragmentShader:ot.backgroundCube_frag},cube:{uniforms:{tCube:{value:null},tFlip:{value:-1},opacity:{value:1}},vertexShader:ot.cube_vert,fragmentShader:ot.cube_frag},equirect:{uniforms:{tEquirect:{value:null}},vertexShader:ot.equirect_vert,fragmentShader:ot.equirect_frag},distance:{uniforms:An([Ee.common,Ee.displacementmap,{referencePosition:{value:new P},nearDistance:{value:1},farDistance:{value:1e3}}]),vertexShader:ot.distance_vert,fragmentShader:ot.distance_frag},shadow:{uniforms:An([Ee.lights,Ee.fog,{color:{value:new Be(0)},opacity:{value:1}}]),vertexShader:ot.shadow_vert,fragmentShader:ot.shadow_frag}};ki.physical={uniforms:An([ki.standard.uniforms,{clearcoat:{value:0},clearcoatMap:{value:null},clearcoatMapTransform:{value:new et},clearcoatNormalMap:{value:null},clearcoatNormalMapTransform:{value:new et},clearcoatNormalScale:{value:new ae(1,1)},clearcoatRoughness:{value:0},clearcoatRoughnessMap:{value:null},clearcoatRoughnessMapTransform:{value:new et},dispersion:{value:0},retroreflectivity:{value:0},iridescence:{value:0},iridescenceMap:{value:null},iridescenceMapTransform:{value:new et},iridescenceIOR:{value:1.3},iridescenceThicknessMinimum:{value:100},iridescenceThicknessMaximum:{value:400},iridescenceThicknessMap:{value:null},iridescenceThicknessMapTransform:{value:new et},sheen:{value:0},sheenColor:{value:new Be(0)},sheenColorMap:{value:null},sheenColorMapTransform:{value:new et},sheenRoughness:{value:1},sheenRoughnessMap:{value:null},sheenRoughnessMapTransform:{value:new et},transmission:{value:0},transmissionMap:{value:null},transmissionMapTransform:{value:new et},transmissionSamplerSize:{value:new ae},transmissionSamplerMap:{value:null},thickness:{value:0},thicknessMap:{value:null},thicknessMapTransform:{value:new et},attenuationDistance:{value:0},attenuationColor:{value:new Be(0)},specularColor:{value:new Be(1,1,1)},specularColorMap:{value:null},specularColorMapTransform:{value:new et},specularIntensity:{value:1},specularIntensityMap:{value:null},specularIntensityMapTransform:{value:new et},anisotropyVector:{value:new ae},anisotropyMap:{value:null},anisotropyMapTransform:{value:new et}}]),vertexShader:ot.meshphysical_vert,fragmentShader:ot.meshphysical_frag};var Tl={r:0,b:0,g:0},c_=new Ze,Kp=new et;Kp.set(-1,0,0,0,1,0,0,0,1);function l_(i,e,t,n,s,r){let a=new Be(0),o=s===!0?0:1,c,l,h=null,u=0,d=null;function f(_){let S=_.isScene===!0?_.background:null;if(S&&S.isTexture){let x=_.backgroundBlurriness>0;S=e.get(S,x)}return S}function m(_){let S=!1,x=f(_);x===null?g(a,o):x&&x.isColor&&(g(x,1),S=!0);let M=i.xr.getEnvironmentBlendMode();M==="additive"?t.buffers.color.setClear(0,0,0,1,r):M==="alpha-blend"&&t.buffers.color.setClear(0,0,0,0,r),(i.autoClear||S)&&(t.buffers.depth.setTest(!0),t.buffers.depth.setMask(!0),t.buffers.color.setMask(!0),i.clear(i.autoClearColor,i.autoClearDepth,i.autoClearStencil))}function b(_,S){let x=f(S);x&&(x.isCubeTexture||x.mapping===to)?(l===void 0&&(l=new ke(new tn(1,1,1),new Wn({name:"BackgroundCubeMaterial",uniforms:ir(ki.backgroundCube.uniforms),vertexShader:ki.backgroundCube.vertexShader,fragmentShader:ki.backgroundCube.fragmentShader,side:yn,depthTest:!1,depthWrite:!1,fog:!1,allowOverride:!1})),l.geometry.deleteAttribute("normal"),l.geometry.deleteAttribute("uv"),l.onBeforeRender=function(M,E,A){this.matrixWorld.copyPosition(A.matrixWorld)},Object.defineProperty(l.material,"envMap",{get:function(){return this.uniforms.envMap.value}}),n.update(l)),l.material.uniforms.envMap.value=x,l.material.uniforms.backgroundBlurriness.value=S.backgroundBlurriness,l.material.uniforms.backgroundIntensity.value=S.backgroundIntensity,l.material.uniforms.backgroundRotation.value.setFromMatrix4(c_.makeRotationFromEuler(S.backgroundRotation)).transpose(),x.isCubeTexture&&x.isRenderTargetTexture===!1&&l.material.uniforms.backgroundRotation.value.premultiply(Kp),l.material.toneMapped=lt.getTransfer(x.colorSpace)!==wt,(h!==x||u!==x.version||d!==i.toneMapping)&&(l.material.needsUpdate=!0,h=x,u=x.version,d=i.toneMapping),l.layers.enableAll(),_.unshift(l,l.geometry,l.material,0,0,null)):x&&x.isTexture&&(c===void 0&&(c=new ke(new Vn(2,2),new Wn({name:"BackgroundMaterial",uniforms:ir(ki.background.uniforms),vertexShader:ki.background.vertexShader,fragmentShader:ki.background.fragmentShader,side:Ui,depthTest:!1,depthWrite:!1,fog:!1,allowOverride:!1})),c.geometry.deleteAttribute("normal"),Object.defineProperty(c.material,"map",{get:function(){return this.uniforms.t2D.value}}),n.update(c)),c.material.uniforms.t2D.value=x,c.material.uniforms.backgroundIntensity.value=S.backgroundIntensity,c.material.toneMapped=lt.getTransfer(x.colorSpace)!==wt,x.matrixAutoUpdate===!0&&x.updateMatrix(),c.material.uniforms.uvTransform.value.copy(x.matrix),(h!==x||u!==x.version||d!==i.toneMapping)&&(c.material.needsUpdate=!0,h=x,u=x.version,d=i.toneMapping),c.layers.enableAll(),_.unshift(c,c.geometry,c.material,0,0,null))}function g(_,S){_.getRGB(Tl,mu(i)),t.buffers.color.setClear(Tl.r,Tl.g,Tl.b,S,r)}function p(){l!==void 0&&(l.geometry.dispose(),l.material.dispose(),l=void 0),c!==void 0&&(c.geometry.dispose(),c.material.dispose(),c=void 0)}return{getClearColor:function(){return a},setClearColor:function(_,S=1){a.set(_),o=S,g(a,o)},getClearAlpha:function(){return o},setClearAlpha:function(_){o=_,g(a,o)},render:m,addToRenderList:b,dispose:p}}function h_(i,e){let t=i.getParameter(i.MAX_VERTEX_ATTRIBS),n={},s=d(null),r=s,a=!1;function o(I,L,F,D,k){let W=!1,X=u(I,D,F,L);r!==X&&(r=X,l(r.object)),W=f(I,D,F,k),W&&m(I,D,F,k),k!==null&&e.update(k,i.ELEMENT_ARRAY_BUFFER),(W||a)&&(a=!1,x(I,L,F,D),k!==null&&i.bindBuffer(i.ELEMENT_ARRAY_BUFFER,e.get(k).buffer))}function c(){return i.createVertexArray()}function l(I){return i.bindVertexArray(I)}function h(I){return i.deleteVertexArray(I)}function u(I,L,F,D){let k=D.wireframe===!0,W=n[L.id];W===void 0&&(W={},n[L.id]=W);let X=I.isInstancedMesh===!0?I.id:0,re=W[X];re===void 0&&(re={},W[X]=re);let j=re[F.id];j===void 0&&(j={},re[F.id]=j);let ee=j[k];return ee===void 0&&(ee=d(c()),j[k]=ee),ee}function d(I){let L=[],F=[],D=[];for(let k=0;k<t;k++)L[k]=0,F[k]=0,D[k]=0;return{geometry:null,program:null,wireframe:!1,newAttributes:L,enabledAttributes:F,attributeDivisors:D,object:I,attributes:{},index:null}}function f(I,L,F,D){let k=r.attributes,W=L.attributes,X=0,re=F.getAttributes();for(let j in re)if(re[j].location>=0){let te=k[j],Ne=W[j];if(Ne===void 0&&(j==="instanceMatrix"&&I.instanceMatrix&&(Ne=I.instanceMatrix),j==="instanceColor"&&I.instanceColor&&(Ne=I.instanceColor)),te===void 0||te.attribute!==Ne||Ne&&te.data!==Ne.data)return!0;X++}return r.attributesNum!==X||r.index!==D}function m(I,L,F,D){let k={},W=L.attributes,X=0,re=F.getAttributes();for(let j in re)if(re[j].location>=0){let te=W[j];te===void 0&&(j==="instanceMatrix"&&I.instanceMatrix&&(te=I.instanceMatrix),j==="instanceColor"&&I.instanceColor&&(te=I.instanceColor));let Ne={};Ne.attribute=te,te&&te.data&&(Ne.data=te.data),k[j]=Ne,X++}r.attributes=k,r.attributesNum=X,r.index=D}function b(){let I=r.newAttributes;for(let L=0,F=I.length;L<F;L++)I[L]=0}function g(I){p(I,0)}function p(I,L){let F=r.newAttributes,D=r.enabledAttributes,k=r.attributeDivisors;F[I]=1,D[I]===0&&(i.enableVertexAttribArray(I),D[I]=1),k[I]!==L&&(i.vertexAttribDivisor(I,L),k[I]=L)}function _(){let I=r.newAttributes,L=r.enabledAttributes;for(let F=0,D=L.length;F<D;F++)L[F]!==I[F]&&(i.disableVertexAttribArray(F),L[F]=0)}function S(I,L,F,D,k,W,X){X===!0?i.vertexAttribIPointer(I,L,F,k,W):i.vertexAttribPointer(I,L,F,D,k,W)}function x(I,L,F,D){b();let k=D.attributes,W=F.getAttributes(),X=L.defaultAttributeValues;for(let re in W){let j=W[re];if(j.location>=0){let ee=k[re];if(ee===void 0&&(re==="instanceMatrix"&&I.instanceMatrix&&(ee=I.instanceMatrix),re==="instanceColor"&&I.instanceColor&&(ee=I.instanceColor)),ee!==void 0){let te=ee.normalized,Ne=ee.itemSize,Se=e.get(ee);if(Se===void 0)continue;let ct=Se.buffer,$e=Se.type,ht=Se.bytesPerElement,J=$e===i.INT||$e===i.UNSIGNED_INT||ee.gpuType===zc;if(ee.isInterleavedBufferAttribute){let ie=ee.data,ve=ie.stride,Ke=ee.offset;if(ie.isInstancedInterleavedBuffer){for(let Ae=0;Ae<j.locationSize;Ae++)p(j.location+Ae,ie.meshPerAttribute);I.isInstancedMesh!==!0&&D._maxInstanceCount===void 0&&(D._maxInstanceCount=ie.meshPerAttribute*ie.count)}else for(let Ae=0;Ae<j.locationSize;Ae++)g(j.location+Ae);i.bindBuffer(i.ARRAY_BUFFER,ct);for(let Ae=0;Ae<j.locationSize;Ae++)S(j.location+Ae,Ne/j.locationSize,$e,te,ve*ht,(Ke+Ne/j.locationSize*Ae)*ht,J)}else{if(ee.isInstancedBufferAttribute){for(let ie=0;ie<j.locationSize;ie++)p(j.location+ie,ee.meshPerAttribute);I.isInstancedMesh!==!0&&D._maxInstanceCount===void 0&&(D._maxInstanceCount=ee.meshPerAttribute*ee.count)}else for(let ie=0;ie<j.locationSize;ie++)g(j.location+ie);i.bindBuffer(i.ARRAY_BUFFER,ct);for(let ie=0;ie<j.locationSize;ie++)S(j.location+ie,Ne/j.locationSize,$e,te,Ne*ht,Ne/j.locationSize*ie*ht,J)}}else if(X!==void 0){let te=X[re];if(te!==void 0)switch(te.length){case 2:i.vertexAttrib2fv(j.location,te);break;case 3:i.vertexAttrib3fv(j.location,te);break;case 4:i.vertexAttrib4fv(j.location,te);break;default:i.vertexAttrib1fv(j.location,te)}}}}_()}function M(){w();for(let I in n){let L=n[I];for(let F in L){let D=L[F];for(let k in D){let W=D[k];for(let X in W)h(W[X].object),delete W[X];delete D[k]}}delete n[I]}}function E(I){if(n[I.id]===void 0)return;let L=n[I.id];for(let F in L){let D=L[F];for(let k in D){let W=D[k];for(let X in W)h(W[X].object),delete W[X];delete D[k]}}delete n[I.id]}function A(I){for(let L in n){let F=n[L];for(let D in F){let k=F[D];if(k[I.id]===void 0)continue;let W=k[I.id];for(let X in W)h(W[X].object),delete W[X];delete k[I.id]}}}function y(I){for(let L in n){let F=n[L],D=I.isInstancedMesh===!0?I.id:0,k=F[D];if(k!==void 0){for(let W in k){let X=k[W];for(let re in X)h(X[re].object),delete X[re];delete k[W]}delete F[D],Object.keys(F).length===0&&delete n[L]}}}function w(){R(),a=!0,r!==s&&(r=s,l(r.object))}function R(){s.geometry=null,s.program=null,s.wireframe=!1}return{setup:o,reset:w,resetDefaultState:R,dispose:M,releaseStatesOfGeometry:E,releaseStatesOfObject:y,releaseStatesOfProgram:A,initAttributes:b,enableAttribute:g,disableUnusedAttributes:_}}function u_(i,e,t){let n;function s(c){n=c}function r(c,l){i.drawArrays(n,c,l),t.update(l,n,1)}function a(c,l,h){h!==0&&(i.drawArraysInstanced(n,c,l,h),t.update(l,n,h))}function o(c,l,h){if(h===0)return;e.get("WEBGL_multi_draw").multiDrawArraysWEBGL(n,c,0,l,0,h);let d=0;for(let f=0;f<h;f++)d+=l[f];t.update(d,n,1)}this.setMode=s,this.render=r,this.renderInstances=a,this.renderMultiDraw=o}function d_(i,e,t,n){let s;function r(){if(s!==void 0)return s;if(e.has("EXT_texture_filter_anisotropic")===!0){let A=e.get("EXT_texture_filter_anisotropic");s=i.getParameter(A.MAX_TEXTURE_MAX_ANISOTROPY_EXT)}else s=0;return s}function a(A){return!(A!==jn&&n.convert(A)!==i.getParameter(i.IMPLEMENTATION_COLOR_READ_FORMAT))}function o(A){let y=A===Si&&(e.has("EXT_color_buffer_half_float")||e.has("EXT_color_buffer_float"));return!(A!==Un&&A!==Xn&&!y&&n.convert(A)!==i.getParameter(i.IMPLEMENTATION_COLOR_READ_TYPE))}function c(A){if(A==="highp"){if(i.getShaderPrecisionFormat(i.VERTEX_SHADER,i.HIGH_FLOAT).precision>0&&i.getShaderPrecisionFormat(i.FRAGMENT_SHADER,i.HIGH_FLOAT).precision>0)return"highp";A="mediump"}return A==="mediump"&&i.getShaderPrecisionFormat(i.VERTEX_SHADER,i.MEDIUM_FLOAT).precision>0&&i.getShaderPrecisionFormat(i.FRAGMENT_SHADER,i.MEDIUM_FLOAT).precision>0?"mediump":"lowp"}let l=t.precision!==void 0?t.precision:"highp",h=c(l);h!==l&&(Ve("WebGLRenderer:",l,"not supported, using",h,"instead."),l=h);let u=t.logarithmicDepthBuffer===!0,d=t.reversedDepthBuffer===!0&&e.has("EXT_clip_control");t.reversedDepthBuffer===!0&&d===!1&&Ve("WebGLRenderer: Unable to use reversed depth buffer due to missing EXT_clip_control extension. Fallback to default depth buffer.");let f=i.getParameter(i.MAX_TEXTURE_IMAGE_UNITS),m=i.getParameter(i.MAX_VERTEX_TEXTURE_IMAGE_UNITS),b=i.getParameter(i.MAX_TEXTURE_SIZE),g=i.getParameter(i.MAX_CUBE_MAP_TEXTURE_SIZE),p=i.getParameter(i.MAX_VERTEX_ATTRIBS),_=i.getParameter(i.MAX_VERTEX_UNIFORM_VECTORS),S=i.getParameter(i.MAX_VARYING_VECTORS),x=i.getParameter(i.MAX_FRAGMENT_UNIFORM_VECTORS),M=i.getParameter(i.MAX_SAMPLES),E=i.getParameter(i.SAMPLES);return{isWebGL2:!0,getMaxAnisotropy:r,getMaxPrecision:c,textureFormatReadable:a,textureTypeReadable:o,precision:l,logarithmicDepthBuffer:u,reversedDepthBuffer:d,maxTextures:f,maxVertexTextures:m,maxTextureSize:b,maxCubemapSize:g,maxAttributes:p,maxVertexUniforms:_,maxVaryings:S,maxFragmentUniforms:x,maxSamples:M,samples:E}}function f_(i){let e=this,t=null,n=0,s=!1,r=!1,a=new Hn,o=new et,c={value:null,needsUpdate:!1};this.uniform=c,this.numPlanes=0,this.numIntersection=0,this.init=function(u,d){let f=u.length!==0||d||n!==0||s;return s=d,n=u.length,f},this.beginShadows=function(){r=!0,h(null)},this.endShadows=function(){r=!1},this.setGlobalState=function(u,d){t=h(u,d,0)},this.setState=function(u,d,f){let m=u.clippingPlanes,b=u.clipIntersection,g=u.clipShadows,p=i.get(u);if(!s||m===null||m.length===0||r&&!g)r?h(null):l();else{let _=r?0:n,S=_*4,x=p.clippingState||null;c.value=x,x=h(m,d,S,f);for(let M=0;M!==S;++M)x[M]=t[M];p.clippingState=x,this.numIntersection=b?this.numPlanes:0,this.numPlanes+=_}};function l(){c.value!==t&&(c.value=t,c.needsUpdate=n>0),e.numPlanes=n,e.numIntersection=0}function h(u,d,f,m){let b=u!==null?u.length:0,g=null;if(b!==0){if(g=c.value,m!==!0||g===null){let p=f+b*4,_=d.matrixWorldInverse;o.getNormalMatrix(_),(g===null||g.length<p)&&(g=new Float32Array(p));for(let S=0,x=f;S!==b;++S,x+=4)a.copy(u[S]).applyMatrix4(_,o),a.normal.toArray(g,x),g[x+3]=a.constant}c.value=g,c.needsUpdate=!0}return e.numPlanes=b,e.numIntersection=0,g}}var Qr=4,p_=6,m_=20,g_=256,uo=new Es,wp=new Be,_u=null,yu=0,vu=0,Mu=!1,b_=new P,sr=new P,ta=class{constructor(e){this._renderer=e,this._pingPongRenderTarget=null,this._lodMax=0,this._cubeSize=0,this._sizeLods=[],this._lodMeshes=[],this._backgroundBox=null,this._cubemapMaterial=null,this._equirectMaterial=null,this._blurMaterial=null,this._ggxMaterial=null}fromScene(e,t=0,n=.1,s=100,r={}){let{size:a=256,position:o=b_}=r;_u=this._renderer.getRenderTarget(),yu=this._renderer.getActiveCubeFace(),vu=this._renderer.getActiveMipmapLevel(),Mu=this._renderer.xr.enabled,this._renderer.xr.enabled=!1,this._setSize(a);let c=this._allocateTargets();return c.depthBuffer=!0,this._sceneToCubeUV(e,n,s,c,o),t>0&&this._blur(c,0,0,t),this._applyPMREM(c),this._cleanup(c),c}fromEquirectangular(e,t=null){return this._fromTexture(e,t)}fromCubemap(e,t=null){return this._fromTexture(e,t)}compileCubemapShader(){this._cubemapMaterial===null&&(this._cubemapMaterial=Cp(),this._compileMaterial(this._cubemapMaterial))}compileEquirectangularShader(){this._equirectMaterial===null&&(this._equirectMaterial=Rp(),this._compileMaterial(this._equirectMaterial))}dispose(){this._dispose(),this._cubemapMaterial!==null&&this._cubemapMaterial.dispose(),this._equirectMaterial!==null&&this._equirectMaterial.dispose(),this._backgroundBox!==null&&(this._backgroundBox.geometry.dispose(),this._backgroundBox.material.dispose())}_setSize(e){this._lodMax=Math.floor(Math.log2(e)),this._cubeSize=Math.pow(2,this._lodMax)}_dispose(){this._blurMaterial!==null&&this._blurMaterial.dispose(),this._ggxMaterial!==null&&this._ggxMaterial.dispose(),this._pingPongRenderTarget!==null&&this._pingPongRenderTarget.dispose();for(let e=0;e<this._lodMeshes.length;e++)this._lodMeshes[e].geometry.dispose()}_cleanup(e){this._renderer.setRenderTarget(_u,yu,vu),this._renderer.xr.enabled=Mu,e.scissorTest=!1,$r(e,0,0,e.width,e.height)}_fromTexture(e,t){e.mapping===Rs||e.mapping===tr?this._setSize(e.image.length===0?16:e.image[0].width||e.image[0].image.width):this._setSize(e.image.width/4),_u=this._renderer.getRenderTarget(),yu=this._renderer.getActiveCubeFace(),vu=this._renderer.getActiveMipmapLevel(),Mu=this._renderer.xr.enabled,this._renderer.xr.enabled=!1;let n=t||this._allocateTargets();return this._textureToCubeUV(e,n),this._applyPMREM(n),this._cleanup(n),n}_allocateTargets(){let e=3*Math.max(this._cubeSize,112),t=4*this._cubeSize,n={magFilter:Xt,minFilter:Xt,generateMipmaps:!1,type:Si,format:jn,colorSpace:Dn,depthBuffer:!1},s=Ap(e,t,n);if(this._pingPongRenderTarget===null||this._pingPongRenderTarget.width!==e||this._pingPongRenderTarget.height!==t){this._pingPongRenderTarget!==null&&this._dispose(),this._pingPongRenderTarget=Ap(e,t,n);let{_lodMax:r}=this;({lodMeshes:this._lodMeshes,sizeLods:this._sizeLods}=x_(r)),this._blurMaterial=y_(r,e,t),this._ggxMaterial=__(r,e,t)}return s}_compileMaterial(e){let t=new ke(new Bt,e);this._renderer.compile(t,uo)}_sceneToCubeUV(e,t,n,s,r){let c=new Yt(90,1,t,n),l=[1,-1,1,1,1,1],h=[1,1,1,-1,-1,-1],u=this._renderer,d=u.autoClear,f=u.toneMapping;u.getClearColor(wp),u.toneMapping=vi,u.autoClear=!1,u.state.buffers.depth.getReversed()&&(u.setRenderTarget(s),u.clearDepth(),u.setRenderTarget(null)),this._backgroundBox===null&&(this._backgroundBox=new ke(new tn,new jt({name:"PMREM.Background",side:yn,depthWrite:!1,depthTest:!1})));let b=this._backgroundBox,g=b.material,p=!1,_=e.background;_?_.isColor&&(g.color.copy(_),e.background=null,p=!0):(g.color.copy(wp),p=!0);for(let S=0;S<6;S++){let x=S%3;x===0?(c.up.set(0,l[S],0),c.position.set(r.x,r.y,r.z),c.lookAt(r.x+h[S],r.y,r.z)):x===1?(c.up.set(0,0,l[S]),c.position.set(r.x,r.y,r.z),c.lookAt(r.x,r.y+h[S],r.z)):(c.up.set(0,l[S],0),c.position.set(r.x,r.y,r.z),c.lookAt(r.x,r.y,r.z+h[S]));let M=this._cubeSize;$r(s,x*M,S>2?M:0,M,M),u.setRenderTarget(s),p&&u.render(b,c),u.render(e,c)}u.toneMapping=f,u.autoClear=d,e.background=_}_textureToCubeUV(e,t){let n=this._renderer,s=e.mapping===Rs||e.mapping===tr;s?(this._cubemapMaterial===null&&(this._cubemapMaterial=Cp()),this._cubemapMaterial.uniforms.flipEnvMap.value=e.isRenderTargetTexture===!1?-1:1):this._equirectMaterial===null&&(this._equirectMaterial=Rp());let r=s?this._cubemapMaterial:this._equirectMaterial,a=this._lodMeshes[0];a.material=r;let o=r.uniforms;o.envMap.value=e;let c=this._cubeSize;$r(t,0,0,3*c,2*c),n.setRenderTarget(t),n.render(a,uo)}_applyPMREM(e){let t=this._renderer,n=t.autoClear;t.autoClear=!1;let s=this._lodMeshes.length;for(let r=1;r<s;r++)this._applyGGXFilter(e,r-1,r);t.autoClear=n}_applyGGXFilter(e,t,n){let s=this._renderer,r=this._pingPongRenderTarget,a=this._ggxMaterial,o=this._lodMeshes[n];o.material=a;let c=a.uniforms,l=n/(this._lodMeshes.length-1),h=t/(this._lodMeshes.length-1),u=Math.sqrt(l*l-h*h),d=l*1.25,f=u*d,{_lodMax:m}=this,b=this._sizeLods[n],g=3*b*(n>m-Qr?n-m+Qr:0),p=4*(this._cubeSize-b);c.envMap.value=e.texture,c.roughness.value=f,c.mipInt.value=m-t,$r(r,g,p,3*b,2*b),s.setRenderTarget(r),s.render(o,uo),c.envMap.value=r.texture,c.roughness.value=0,c.mipInt.value=m-n,$r(e,g,p,3*b,2*b),s.setRenderTarget(e),s.render(o,uo)}_blur(e,t,n,s){let r=this._pingPongRenderTarget,a=Math.min(s,Math.PI)/Math.SQRT2;this._blurPass(e,r,t,n,a),this._blurPass(r,e,n,n,a)}_blurPass(e,t,n,s,r){let a=this._renderer,o=this._blurMaterial,c=this._lodMeshes[s];c.material=o;let l=o.uniforms;l.envMap.value=e.texture,l.sigma.value=r,l.mipInt.value=this._lodMax-n;let h=this._sizeLods[s],u=3*h*(s>this._lodMax-Qr?s-this._lodMax+Qr:0),d=4*(this._cubeSize-h);$r(t,u,d,3*h,2*h),a.setRenderTarget(t),a.render(c,uo)}};function x_(i){let e=[],t=[],n=i,s=i-Qr+1+p_;for(let r=0;r<s;r++){let a=Math.pow(2,n);e.push(a);let o=1/(a-2),c=-o,l=1+o,h=[c,c,l,c,l,l,c,c,l,l,c,l],u=6,d=6,f=3,m=new Float32Array(f*d*u),b=new Float32Array(f*d*u);for(let p=0;p<u;p++){let _=p%3*2/3-1,S=p>2?0:-1,x=[_,S,0,_+2/3,S,0,_+2/3,S+1,0,_,S,0,_+2/3,S+1,0,_,S+1,0];m.set(x,f*d*p);for(let M=0;M<d;M++){let E=h[M*2]*2-1,A=h[M*2+1]*2-1;p===0?sr.set(1,A,E):p===1?sr.set(-E,1,-A):p===2?sr.set(-E,A,1):p===3?sr.set(-1,A,-E):p===4?sr.set(-E,-1,A):sr.set(E,A,-1),sr.toArray(b,(p*d+M)*f)}}let g=new Bt;g.setAttribute("position",new an(m,f)),g.setAttribute("outputDirection",new an(b,f)),t.push(new ke(g,null)),n>Qr&&n--}return{lodMeshes:t,sizeLods:e}}function Ap(i,e,t){let n=new wn(i,e,t);return n.texture.mapping=to,n.texture.name="PMREM.cubeUv",n.scissorTest=!0,n}function $r(i,e,t,n,s){i.viewport.set(e,t,n,s),i.scissor.set(e,t,n,s)}function __(i,e,t){return new Wn({name:"PMREMGGXConvolution",defines:{GGX_SAMPLES:g_,CUBEUV_TEXEL_WIDTH:1/e,CUBEUV_TEXEL_HEIGHT:1/t,CUBEUV_MAX_MIP:`${i}.0`},uniforms:{envMap:{value:null},roughness:{value:0},mipInt:{value:0}},vertexShader:Cl(),fragmentShader:`

			precision highp float;
			precision highp int;

			varying vec3 vOutputDirection;

			uniform sampler2D envMap;
			uniform float roughness;
			uniform float mipInt;

			#define ENVMAP_TYPE_CUBE_UV
			#include <cube_uv_reflection_fragment>

			#define PI 3.14159265359

			// Van der Corput radical inverse
			float radicalInverse_VdC(uint bits) {
				bits = (bits << 16u) | (bits >> 16u);
				bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
				bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
				bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
				bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
				return float(bits) * 2.3283064365386963e-10; // / 0x100000000
			}

			// Hammersley sequence
			vec2 hammersley(uint i, uint N) {
				return vec2(float(i) / float(N), radicalInverse_VdC(i));
			}

			// GGX VNDF importance sampling (Eric Heitz 2018)
			// "Sampling the GGX Distribution of Visible Normals"
			// https://jcgt.org/published/0007/04/01/
			vec3 importanceSampleGGX_VNDF(vec2 Xi, vec3 V, float roughness) {
				float alpha = roughness * roughness;

				// Section 4.1: Orthonormal basis
				vec3 T1 = vec3(1.0, 0.0, 0.0);
				vec3 T2 = cross(V, T1);

				// Section 4.2: Parameterization of projected area
				float r = sqrt(Xi.x);
				float phi = 2.0 * PI * Xi.y;
				float t1 = r * cos(phi);
				float t2 = r * sin(phi);
				float s = 0.5 * (1.0 + V.z);
				t2 = (1.0 - s) * sqrt(1.0 - t1 * t1) + s * t2;

				// Section 4.3: Reprojection onto hemisphere
				vec3 Nh = t1 * T1 + t2 * T2 + sqrt(max(0.0, 1.0 - t1 * t1 - t2 * t2)) * V;

				// Section 3.4: Transform back to ellipsoid configuration
				return normalize(vec3(alpha * Nh.x, alpha * Nh.y, max(0.0, Nh.z)));
			}

			void main() {
				vec3 N = normalize(vOutputDirection);
				vec3 V = N; // Assume view direction equals normal for pre-filtering

				vec3 prefilteredColor = vec3(0.0);
				float totalWeight = 0.0;

				// For very low roughness, just sample the environment directly
				if (roughness < 0.001) {
					gl_FragColor = vec4(bilinearCubeUV(envMap, N, mipInt), 1.0);
					return;
				}

				// Tangent space basis for VNDF sampling
				vec3 up = abs(N.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
				vec3 tangent = normalize(cross(up, N));
				vec3 bitangent = cross(N, tangent);

				for(uint i = 0u; i < uint(GGX_SAMPLES); i++) {
					vec2 Xi = hammersley(i, uint(GGX_SAMPLES));

					// For PMREM, V = N, so in tangent space V is always (0, 0, 1)
					vec3 H_tangent = importanceSampleGGX_VNDF(Xi, vec3(0.0, 0.0, 1.0), roughness);

					// Transform H back to world space
					vec3 H = normalize(tangent * H_tangent.x + bitangent * H_tangent.y + N * H_tangent.z);
					vec3 L = normalize(2.0 * dot(V, H) * H - V);

					float NdotL = max(dot(N, L), 0.0);

					if(NdotL > 0.0) {
						// Sample environment at fixed mip level
						// VNDF importance sampling handles the distribution filtering
						vec3 sampleColor = bilinearCubeUV(envMap, L, mipInt);

						// Weight by NdotL for the split-sum approximation
						// VNDF PDF naturally accounts for the visible microfacet distribution
						prefilteredColor += sampleColor * NdotL;
						totalWeight += NdotL;
					}
				}

				if (totalWeight > 0.0) {
					prefilteredColor = prefilteredColor / totalWeight;
				}

				gl_FragColor = vec4(prefilteredColor, 1.0);
			}
		`,blending:Oi,depthTest:!1,depthWrite:!1})}function y_(i,e,t){return new Wn({name:"SphericalGaussianBlur",defines:{SAMPLES:m_,CUBEUV_TEXEL_WIDTH:1/e,CUBEUV_TEXEL_HEIGHT:1/t,CUBEUV_MAX_MIP:`${i}.0`},uniforms:{envMap:{value:null},sigma:{value:0},mipInt:{value:0}},vertexShader:Cl(),fragmentShader:`

			precision highp float;
			precision highp int;

			varying vec3 vOutputDirection;

			uniform sampler2D envMap;
			uniform float sigma;
			uniform float mipInt;

			#define ENVMAP_TYPE_CUBE_UV
			#include <cube_uv_reflection_fragment>

			#define PI 3.14159265359
			#define GOLDEN_ANGLE 2.39996322973

			void main() {

				if ( sigma == 0.0 ) {

					gl_FragColor = vec4( bilinearCubeUV( envMap, vOutputDirection, mipInt ), 1.0 );
					return;

				}

				vec3 outputDirection = normalize( vOutputDirection );

				vec3 up = abs( outputDirection.z ) < 0.999 ? vec3( 0.0, 0.0, 1.0 ) : vec3( 1.0, 0.0, 0.0 );
				vec3 tangent = normalize( cross( up, outputDirection ) );
				vec3 bitangent = cross( outputDirection, tangent );

				// Truncate the kernel at three standard deviations or at the antipode.
				float thetaMax = min( 3.0 * sigma, PI );
				float truncation = 1.0 - exp( - 0.5 * thetaMax * thetaMax / ( sigma * sigma ) );

				vec3 accumColor = vec3( 0.0 );
				float accumWeight = 0.0;

				for ( int i = 0; i < SAMPLES; i ++ ) {

					// Stratified inverse-CDF sampling of the Gaussian, placed on a golden-angle spiral.
					float stratum = ( float( i ) + 0.5 ) / float( SAMPLES );
					float theta = sigma * sqrt( - 2.0 * log( 1.0 - stratum * truncation ) );
					float phi = float( i ) * GOLDEN_ANGLE;

					vec3 offset = cos( phi ) * tangent + sin( phi ) * bitangent;
					vec3 sampleDirection = cos( theta ) * outputDirection + sin( theta ) * offset;

					// Correct the planar sample density to solid angle.
					float weight = sin( theta ) / theta;

					accumColor += weight * bilinearCubeUV( envMap, sampleDirection, mipInt );
					accumWeight += weight;

				}

				gl_FragColor = vec4( accumColor / accumWeight, 1.0 );

			}
		`,blending:Oi,depthTest:!1,depthWrite:!1})}function Rp(){return new Wn({name:"EquirectangularToCubeUV",uniforms:{envMap:{value:null}},vertexShader:Cl(),fragmentShader:`

			precision mediump float;
			precision mediump int;

			varying vec3 vOutputDirection;

			uniform sampler2D envMap;

			#include <common>

			void main() {

				vec3 outputDirection = normalize( vOutputDirection );
				vec2 uv = equirectUv( outputDirection );

				gl_FragColor = vec4( texture2D ( envMap, uv ).rgb, 1.0 );

			}
		`,blending:Oi,depthTest:!1,depthWrite:!1})}function Cp(){return new Wn({name:"CubemapToCubeUV",uniforms:{envMap:{value:null},flipEnvMap:{value:-1}},vertexShader:Cl(),fragmentShader:`

			precision mediump float;
			precision mediump int;

			uniform float flipEnvMap;

			varying vec3 vOutputDirection;

			uniform samplerCube envMap;

			void main() {

				gl_FragColor = textureCube( envMap, vec3( flipEnvMap * vOutputDirection.x, vOutputDirection.yz ) );

			}
		`,blending:Oi,depthTest:!1,depthWrite:!1})}function Cl(){return`

		precision mediump float;
		precision mediump int;

		attribute vec3 outputDirection;

		varying vec3 vOutputDirection;

		void main() {

			vOutputDirection = outputDirection;
			gl_Position = vec4( position, 1.0 );

		}
	`}var Al=class extends wn{constructor(e=1,t={}){super(e,e,t),this.isWebGLCubeRenderTarget=!0;let n={width:e,height:e,depth:1},s=[n,n,n,n,n,n];this.texture=new Na(s),this._setTextureOptions(t),this.texture.isRenderTargetTexture=!0}fromEquirectangularTexture(e,t){this.texture.type=t.type,this.texture.colorSpace=t.colorSpace,this.texture.generateMipmaps=t.generateMipmaps,this.texture.minFilter=t.minFilter,this.texture.magFilter=t.magFilter;let n={uniforms:{tEquirect:{value:null}},vertexShader:`

				varying vec3 vWorldDirection;

				vec3 transformDirection( in vec3 dir, in mat4 matrix ) {

					return normalize( ( matrix * vec4( dir, 0.0 ) ).xyz );

				}

				void main() {

					vWorldDirection = transformDirection( position, modelMatrix );

					#include <begin_vertex>
					#include <project_vertex>

				}
			`,fragmentShader:`

				uniform sampler2D tEquirect;

				varying vec3 vWorldDirection;

				#include <common>

				void main() {

					vec3 direction = normalize( vWorldDirection );

					vec2 sampleUV = equirectUv( direction );

					gl_FragColor = texture2D( tEquirect, sampleUV );

				}
			`},s=new tn(5,5,5),r=new Wn({name:"CubemapFromEquirect",uniforms:ir(n.uniforms),vertexShader:n.vertexShader,fragmentShader:n.fragmentShader,side:yn,blending:Oi});r.uniforms.tEquirect.value=t;let a=new ke(s,r),o=t.minFilter;return t.minFilter===qn&&(t.minFilter=Xt),new Dc(1,10,this).update(e,a),t.minFilter=o,a.geometry.dispose(),a.material.dispose(),this}clear(e,t=!0,n=!0,s=!0){let r=e.getRenderTarget();for(let a=0;a<6;a++)e.setRenderTarget(this,a),e.clear(t,n,s);e.setRenderTarget(r)}};function v_(i){let e=new WeakMap,t=new WeakMap,n=null;function s(d,f=!1){return d==null?null:f?a(d):r(d)}function r(d){if(d&&d.isTexture){let f=d.mapping;if(f===Oc||f===Bc)if(e.has(d)){let m=e.get(d).texture;return o(m,d.mapping)}else{let m=d.image;if(m&&m.height>0){let b=new Al(m.height);return b.fromEquirectangularTexture(i,d),e.set(d,b),d.addEventListener("dispose",l),o(b.texture,d.mapping)}else return null}}return d}function a(d){if(d&&d.isTexture){let f=d.mapping,m=f===Oc||f===Bc,b=f===Rs||f===tr;if(m||b){let g=t.get(d),p=g!==void 0?g.texture.pmremVersion:0;if(d.isRenderTargetTexture&&d.pmremVersion!==p)return n===null&&(n=new ta(i)),g=m?n.fromEquirectangular(d,g):n.fromCubemap(d,g),g.texture.pmremVersion=d.pmremVersion,t.set(d,g),g.texture;if(g!==void 0)return g.texture;{let _=d.image;return m&&_&&_.height>0||b&&_&&c(_)?(n===null&&(n=new ta(i)),g=m?n.fromEquirectangular(d):n.fromCubemap(d),g.texture.pmremVersion=d.pmremVersion,t.set(d,g),d.addEventListener("dispose",h),g.texture):null}}}return d}function o(d,f){return f===Oc?d.mapping=Rs:f===Bc&&(d.mapping=tr),d}function c(d){let f=0,m=6;for(let b=0;b<m;b++)d[b]!==void 0&&f++;return f===m}function l(d){let f=d.target;f.removeEventListener("dispose",l);let m=e.get(f);m!==void 0&&(e.delete(f),m.dispose())}function h(d){let f=d.target;f.removeEventListener("dispose",h);let m=t.get(f);m!==void 0&&(t.delete(f),m.dispose())}function u(){e=new WeakMap,t=new WeakMap,n!==null&&(n.dispose(),n=null)}return{get:s,dispose:u}}function M_(i){let e={};function t(n){if(e[n]!==void 0)return e[n];let s=i.getExtension(n);return e[n]=s,s}return{has:function(n){return t(n)!==null},init:function(){t("EXT_color_buffer_float"),t("WEBGL_clip_cull_distance"),t("OES_texture_float_linear"),t("EXT_color_buffer_half_float"),t("WEBGL_multisampled_render_to_texture"),t("WEBGL_render_shared_exponent")},get:function(n){let s=t(n);return s===null&&Gs("WebGLRenderer: "+n+" extension not supported."),s}}}function S_(i,e,t,n){let s={},r=new WeakMap;function a(u){let d=u.target;d.index!==null&&e.remove(d.index);for(let m in d.attributes)e.remove(d.attributes[m]);d.removeEventListener("dispose",a),delete s[d.id];let f=r.get(d);f&&(e.remove(f),r.delete(d)),n.releaseStatesOfGeometry(d),d.isInstancedBufferGeometry===!0&&delete d._maxInstanceCount,t.memory.geometries--}function o(u,d){return s[d.id]===!0||(d.addEventListener("dispose",a),s[d.id]=!0,t.memory.geometries++),d}function c(u){let d=u.attributes;for(let f in d)e.update(d[f],i.ARRAY_BUFFER)}function l(u){let d=[],f=u.index,m=u.attributes.position,b=0;if(m===void 0)return;if(f!==null){let _=f.array;b=f.version;for(let S=0,x=_.length;S<x;S+=3){let M=_[S+0],E=_[S+1],A=_[S+2];d.push(M,E,E,A,A,M)}}else{let _=m.array;b=m.version;for(let S=0,x=_.length/3-1;S<x;S+=3){let M=S+0,E=S+1,A=S+2;d.push(M,E,E,A,A,M)}}let g=new(m.count>=65535?Ra:Aa)(d,1);g.version=b;let p=r.get(u);p&&e.remove(p),r.set(u,g)}function h(u){let d=r.get(u);if(d){let f=u.index;f!==null&&d.version<f.version&&l(u)}else l(u);return r.get(u)}return{get:o,update:c,getWireframeAttribute:h}}function E_(i,e,t){let n;function s(u){n=u}let r,a;function o(u){r=u.type,a=u.bytesPerElement}function c(u,d){i.drawElements(n,d,r,u*a),t.update(d,n,1)}function l(u,d,f){f!==0&&(i.drawElementsInstanced(n,d,r,u*a,f),t.update(d,n,f))}function h(u,d,f){if(f===0)return;e.get("WEBGL_multi_draw").multiDrawElementsWEBGL(n,d,0,r,u,0,f);let b=0;for(let g=0;g<f;g++)b+=d[g];t.update(b,n,1)}this.setMode=s,this.setIndex=o,this.render=c,this.renderInstances=l,this.renderMultiDraw=h}function T_(i){let e={geometries:0,textures:0},t={frame:0,calls:0,triangles:0,points:0,lines:0};function n(r,a,o){switch(t.calls++,a){case i.TRIANGLES:t.triangles+=o*(r/3);break;case i.LINES:t.lines+=o*(r/2);break;case i.LINE_STRIP:t.lines+=o*(r-1);break;case i.LINE_LOOP:t.lines+=o*r;break;case i.POINTS:t.points+=o*r;break;default:Ye("WebGLInfo: Unknown draw mode:",a);break}}function s(){t.calls=0,t.triangles=0,t.points=0,t.lines=0}return{memory:e,render:t,programs:null,autoReset:!0,reset:s,update:n}}function w_(i,e,t){let n=new WeakMap,s=new Pt;function r(a,o,c){let l=a.morphTargetInfluences,h=o.morphAttributes.position||o.morphAttributes.normal||o.morphAttributes.color,u=h!==void 0?h.length:0,d=n.get(o);if(d===void 0||d.count!==u){let w=function(){A.dispose(),n.delete(o),o.removeEventListener("dispose",w)};d!==void 0&&d.texture.dispose();let f=o.morphAttributes.position!==void 0,m=o.morphAttributes.normal!==void 0,b=o.morphAttributes.color!==void 0,g=o.morphAttributes.position||[],p=o.morphAttributes.normal||[],_=o.morphAttributes.color||[],S=0;f===!0&&(S=1),m===!0&&(S=2),b===!0&&(S=3);let x=o.attributes.position.count*S,M=1;x>e.maxTextureSize&&(M=Math.ceil(x/e.maxTextureSize),x=e.maxTextureSize);let E=new Float32Array(x*M*4*u),A=new wa(E,x,M,u);A.type=Xn,A.needsUpdate=!0;let y=S*4;for(let R=0;R<u;R++){let I=g[R],L=p[R],F=_[R],D=x*M*4*R;for(let k=0;k<I.count;k++){let W=k*y;f===!0&&(s.fromBufferAttribute(I,k),E[D+W+0]=s.x,E[D+W+1]=s.y,E[D+W+2]=s.z,E[D+W+3]=0),m===!0&&(s.fromBufferAttribute(L,k),E[D+W+4]=s.x,E[D+W+5]=s.y,E[D+W+6]=s.z,E[D+W+7]=0),b===!0&&(s.fromBufferAttribute(F,k),E[D+W+8]=s.x,E[D+W+9]=s.y,E[D+W+10]=s.z,E[D+W+11]=F.itemSize===4?s.w:1)}}d={count:u,texture:A,size:new ae(x,M)},n.set(o,d),o.addEventListener("dispose",w)}if(a.isInstancedMesh===!0&&a.morphTexture!==null)c.getUniforms().setValue(i,"morphTexture",a.morphTexture,t);else{let f=0;for(let b=0;b<l.length;b++)f+=l[b];let m=o.morphTargetsRelative?1:1-f;c.getUniforms().setValue(i,"morphTargetBaseInfluence",m),c.getUniforms().setValue(i,"morphTargetInfluences",l)}c.getUniforms().setValue(i,"morphTargetsTexture",d.texture,t),c.getUniforms().setValue(i,"morphTargetsTextureSize",d.size)}return{update:r}}function A_(i,e,t,n,s){let r=new WeakMap;function a(l){let h=s.render.frame,u=l.geometry,d=e.get(l,u);if(r.get(d)!==h&&(e.update(d),r.set(d,h)),l.isInstancedMesh&&(l.hasEventListener("dispose",c)===!1&&l.addEventListener("dispose",c),r.get(l)!==h&&(t.update(l.instanceMatrix,i.ARRAY_BUFFER),l.instanceColor!==null&&t.update(l.instanceColor,i.ARRAY_BUFFER),r.set(l,h))),l.isSkinnedMesh){let f=l.skeleton;r.get(f)!==h&&(f.update(),r.set(f,h))}return d}function o(){r=new WeakMap}function c(l){let h=l.target;h.removeEventListener("dispose",c),n.releaseStatesOfObject(h),t.remove(h.instanceMatrix),h.instanceColor!==null&&t.remove(h.instanceColor)}return{update:a,dispose:o}}var R_={[Zh]:"LINEAR_TONE_MAPPING",[$h]:"REINHARD_TONE_MAPPING",[Qh]:"CINEON_TONE_MAPPING",[eo]:"ACES_FILMIC_TONE_MAPPING",[tu]:"AGX_TONE_MAPPING",[nu]:"NEUTRAL_TONE_MAPPING",[eu]:"CUSTOM_TONE_MAPPING"};function C_(i,e,t,n,s,r){let a=new wn(e,t,{type:i,depthBuffer:s,stencilBuffer:r,samples:n?4:0,storeMultisampledDepthBuffer:!1,storeMultisampledStencilBuffer:!1,resolveDepthBuffer:!1,resolveStencilBuffer:!1}),o=null,c=null,l=new Bt;l.setAttribute("position",new nt([-1,3,0,-1,-1,0,3,-1,0],3)),l.setAttribute("uv",new nt([0,2,0,0,2,0],2));let h=new Sc({uniforms:{tDiffuse:{value:null}},vertexShader:`
			precision highp float;

			uniform mat4 modelViewMatrix;
			uniform mat4 projectionMatrix;

			attribute vec3 position;
			attribute vec2 uv;

			varying vec2 vUv;

			void main() {
				vUv = uv;
				gl_Position = projectionMatrix * modelViewMatrix * vec4( position, 1.0 );
			}`,fragmentShader:`
			precision highp float;

			uniform sampler2D tDiffuse;

			varying vec2 vUv;

			#include <tonemapping_pars_fragment>
			#include <colorspace_pars_fragment>

			void main() {
				gl_FragColor = texture2D( tDiffuse, vUv );

				#ifdef LINEAR_TONE_MAPPING
					gl_FragColor.rgb = LinearToneMapping( gl_FragColor.rgb );
				#elif defined( REINHARD_TONE_MAPPING )
					gl_FragColor.rgb = ReinhardToneMapping( gl_FragColor.rgb );
				#elif defined( CINEON_TONE_MAPPING )
					gl_FragColor.rgb = CineonToneMapping( gl_FragColor.rgb );
				#elif defined( ACES_FILMIC_TONE_MAPPING )
					gl_FragColor.rgb = ACESFilmicToneMapping( gl_FragColor.rgb );
				#elif defined( AGX_TONE_MAPPING )
					gl_FragColor.rgb = AgXToneMapping( gl_FragColor.rgb );
				#elif defined( NEUTRAL_TONE_MAPPING )
					gl_FragColor.rgb = NeutralToneMapping( gl_FragColor.rgb );
				#elif defined( CUSTOM_TONE_MAPPING )
					gl_FragColor.rgb = CustomToneMapping( gl_FragColor.rgb );
				#endif

				#ifdef SRGB_TRANSFER
					gl_FragColor = sRGBTransferOETF( gl_FragColor );
				#endif
			}`,depthTest:!1,depthWrite:!1}),u=new ke(l,h),d=new Es(-1,1,1,-1,0,1),f=null,m=null,b=!1,g,p=null,_=[],S=!1;this.setSize=function(x,M){a.setSize(x,M),o!==null&&o.setSize(x,M),c!==null&&c.setSize(x,M);for(let E=0;E<_.length;E++){let A=_[E];A.setSize&&A.setSize(x,M)}},this.setEffects=function(x){_=x,S=_.length>0&&_[0].isRenderPass===!0;let M=a.width,E=a.height;_.length>0&&o===null&&(o=new wn(M,E,{type:Si,depthBuffer:!1,stencilBuffer:!1}),c=new wn(M,E,{type:Si,depthBuffer:!1,stencilBuffer:!1}));for(let A=0;A<_.length;A++){let y=_[A];y.setSize&&y.setSize(M,E)}},this.begin=function(x,M){if(b||x.toneMapping===vi&&_.length===0)return!1;if(p=M,M!==null){let E=M.width,A=M.height;(a.width!==E||a.height!==A)&&this.setSize(E,A)}return S===!1&&x.setRenderTarget(a),g=x.toneMapping,x.toneMapping=vi,!0},this.hasRenderPass=function(){return S},this.end=function(x,M){x.toneMapping=g,b=!0;let E=a,A=o;for(let y=0;y<_.length;y++){let w=_[y];w.enabled!==!1&&(w.render(x,A,E,M),w.needsSwap!==!1&&(E=A,A=A===o?c:o))}if(f!==x.outputColorSpace||m!==x.toneMapping){f=x.outputColorSpace,m=x.toneMapping,h.defines={},lt.getTransfer(f)===wt&&(h.defines.SRGB_TRANSFER="");let y=R_[m];y&&(h.defines[y]=""),h.needsUpdate=!0}h.uniforms.tDiffuse.value=E.texture,x.setRenderTarget(p),x.render(u,d),p=null,b=!1},this.isCompositing=function(){return b},this.dispose=function(){a.dispose(),o!==null&&o.dispose(),c!==null&&c.dispose(),l.dispose(),h.dispose()}}var Yp=new hn,Tu=new _s(1,1),Jp=new wa,Zp=new pc,$p=new Na,Pp=[],Ip=[],Lp=new Float32Array(16),Dp=new Float32Array(9),Np=new Float32Array(4);function na(i,e,t){let n=i[0];if(n<=0||n>0)return i;let s=e*t,r=Pp[s];if(r===void 0&&(r=new Float32Array(s),Pp[s]=r),e!==0){n.toArray(r,0);for(let a=1,o=0;a!==e;++a)o+=t,i[a].toArray(r,o)}return r}function un(i,e){if(i.length!==e.length)return!1;for(let t=0,n=i.length;t<n;t++)if(i[t]!==e[t])return!1;return!0}function dn(i,e){for(let t=0,n=e.length;t<n;t++)i[t]=e[t]}function Pl(i,e){let t=Ip[e];t===void 0&&(t=new Int32Array(e),Ip[e]=t);for(let n=0;n!==e;++n)t[n]=i.allocateTextureUnit();return t}function P_(i,e){let t=this.cache;t[0]!==e&&(i.uniform1f(this.addr,e),t[0]=e)}function I_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y)&&(i.uniform2f(this.addr,e.x,e.y),t[0]=e.x,t[1]=e.y);else{if(un(t,e))return;i.uniform2fv(this.addr,e),dn(t,e)}}function L_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y||t[2]!==e.z)&&(i.uniform3f(this.addr,e.x,e.y,e.z),t[0]=e.x,t[1]=e.y,t[2]=e.z);else if(e.r!==void 0)(t[0]!==e.r||t[1]!==e.g||t[2]!==e.b)&&(i.uniform3f(this.addr,e.r,e.g,e.b),t[0]=e.r,t[1]=e.g,t[2]=e.b);else{if(un(t,e))return;i.uniform3fv(this.addr,e),dn(t,e)}}function D_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y||t[2]!==e.z||t[3]!==e.w)&&(i.uniform4f(this.addr,e.x,e.y,e.z,e.w),t[0]=e.x,t[1]=e.y,t[2]=e.z,t[3]=e.w);else{if(un(t,e))return;i.uniform4fv(this.addr,e),dn(t,e)}}function N_(i,e){let t=this.cache,n=e.elements;if(n===void 0){if(un(t,e))return;i.uniformMatrix2fv(this.addr,!1,e),dn(t,e)}else{if(un(t,n))return;Np.set(n),i.uniformMatrix2fv(this.addr,!1,Np),dn(t,n)}}function F_(i,e){let t=this.cache,n=e.elements;if(n===void 0){if(un(t,e))return;i.uniformMatrix3fv(this.addr,!1,e),dn(t,e)}else{if(un(t,n))return;Dp.set(n),i.uniformMatrix3fv(this.addr,!1,Dp),dn(t,n)}}function U_(i,e){let t=this.cache,n=e.elements;if(n===void 0){if(un(t,e))return;i.uniformMatrix4fv(this.addr,!1,e),dn(t,e)}else{if(un(t,n))return;Lp.set(n),i.uniformMatrix4fv(this.addr,!1,Lp),dn(t,n)}}function O_(i,e){let t=this.cache;t[0]!==e&&(i.uniform1i(this.addr,e),t[0]=e)}function B_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y)&&(i.uniform2i(this.addr,e.x,e.y),t[0]=e.x,t[1]=e.y);else{if(un(t,e))return;i.uniform2iv(this.addr,e),dn(t,e)}}function k_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y||t[2]!==e.z)&&(i.uniform3i(this.addr,e.x,e.y,e.z),t[0]=e.x,t[1]=e.y,t[2]=e.z);else{if(un(t,e))return;i.uniform3iv(this.addr,e),dn(t,e)}}function z_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y||t[2]!==e.z||t[3]!==e.w)&&(i.uniform4i(this.addr,e.x,e.y,e.z,e.w),t[0]=e.x,t[1]=e.y,t[2]=e.z,t[3]=e.w);else{if(un(t,e))return;i.uniform4iv(this.addr,e),dn(t,e)}}function H_(i,e){let t=this.cache;t[0]!==e&&(i.uniform1ui(this.addr,e),t[0]=e)}function G_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y)&&(i.uniform2ui(this.addr,e.x,e.y),t[0]=e.x,t[1]=e.y);else{if(un(t,e))return;i.uniform2uiv(this.addr,e),dn(t,e)}}function V_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y||t[2]!==e.z)&&(i.uniform3ui(this.addr,e.x,e.y,e.z),t[0]=e.x,t[1]=e.y,t[2]=e.z);else{if(un(t,e))return;i.uniform3uiv(this.addr,e),dn(t,e)}}function W_(i,e){let t=this.cache;if(e.x!==void 0)(t[0]!==e.x||t[1]!==e.y||t[2]!==e.z||t[3]!==e.w)&&(i.uniform4ui(this.addr,e.x,e.y,e.z,e.w),t[0]=e.x,t[1]=e.y,t[2]=e.z,t[3]=e.w);else{if(un(t,e))return;i.uniform4uiv(this.addr,e),dn(t,e)}}function q_(i,e,t){let n=this.cache,s=t.allocateTextureUnit();n[0]!==s&&(i.uniform1i(this.addr,s),n[0]=s);let r;this.type===i.SAMPLER_2D_SHADOW?(Tu.compareFunction=t.isReversedDepthBuffer()?El:Sl,r=Tu):r=Yp,t.setTexture2D(e||r,s)}function X_(i,e,t){let n=this.cache,s=t.allocateTextureUnit();n[0]!==s&&(i.uniform1i(this.addr,s),n[0]=s),t.setTexture3D(e||Zp,s)}function j_(i,e,t){let n=this.cache,s=t.allocateTextureUnit();n[0]!==s&&(i.uniform1i(this.addr,s),n[0]=s),t.setTextureCube(e||$p,s)}function K_(i,e,t){let n=this.cache,s=t.allocateTextureUnit();n[0]!==s&&(i.uniform1i(this.addr,s),n[0]=s),t.setTexture2DArray(e||Jp,s)}function Y_(i){switch(i){case 5126:return P_;case 35664:return I_;case 35665:return L_;case 35666:return D_;case 35674:return N_;case 35675:return F_;case 35676:return U_;case 5124:case 35670:return O_;case 35667:case 35671:return B_;case 35668:case 35672:return k_;case 35669:case 35673:return z_;case 5125:return H_;case 36294:return G_;case 36295:return V_;case 36296:return W_;case 35678:case 36198:case 36298:case 36306:case 35682:return q_;case 35679:case 36299:case 36307:return X_;case 35680:case 36300:case 36308:case 36293:return j_;case 36289:case 36303:case 36311:case 36292:return K_}}function J_(i,e){i.uniform1fv(this.addr,e)}function Z_(i,e){let t=na(e,this.size,2);i.uniform2fv(this.addr,t)}function $_(i,e){let t=na(e,this.size,3);i.uniform3fv(this.addr,t)}function Q_(i,e){let t=na(e,this.size,4);i.uniform4fv(this.addr,t)}function e1(i,e){let t=na(e,this.size,4);i.uniformMatrix2fv(this.addr,!1,t)}function t1(i,e){let t=na(e,this.size,9);i.uniformMatrix3fv(this.addr,!1,t)}function n1(i,e){let t=na(e,this.size,16);i.uniformMatrix4fv(this.addr,!1,t)}function i1(i,e){i.uniform1iv(this.addr,e)}function s1(i,e){i.uniform2iv(this.addr,e)}function r1(i,e){i.uniform3iv(this.addr,e)}function a1(i,e){i.uniform4iv(this.addr,e)}function o1(i,e){i.uniform1uiv(this.addr,e)}function c1(i,e){i.uniform2uiv(this.addr,e)}function l1(i,e){i.uniform3uiv(this.addr,e)}function h1(i,e){i.uniform4uiv(this.addr,e)}function u1(i,e,t){let n=this.cache,s=e.length,r=Pl(t,s);un(n,r)||(i.uniform1iv(this.addr,r),dn(n,r));let a;this.type===i.SAMPLER_2D_SHADOW?a=Tu:a=Yp;for(let o=0;o!==s;++o)t.setTexture2D(e[o]||a,r[o])}function d1(i,e,t){let n=this.cache,s=e.length,r=Pl(t,s);un(n,r)||(i.uniform1iv(this.addr,r),dn(n,r));for(let a=0;a!==s;++a)t.setTexture3D(e[a]||Zp,r[a])}function f1(i,e,t){let n=this.cache,s=e.length,r=Pl(t,s);un(n,r)||(i.uniform1iv(this.addr,r),dn(n,r));for(let a=0;a!==s;++a)t.setTextureCube(e[a]||$p,r[a])}function p1(i,e,t){let n=this.cache,s=e.length,r=Pl(t,s);un(n,r)||(i.uniform1iv(this.addr,r),dn(n,r));for(let a=0;a!==s;++a)t.setTexture2DArray(e[a]||Jp,r[a])}function m1(i){switch(i){case 5126:return J_;case 35664:return Z_;case 35665:return $_;case 35666:return Q_;case 35674:return e1;case 35675:return t1;case 35676:return n1;case 5124:case 35670:return i1;case 35667:case 35671:return s1;case 35668:case 35672:return r1;case 35669:case 35673:return a1;case 5125:return o1;case 36294:return c1;case 36295:return l1;case 36296:return h1;case 35678:case 36198:case 36298:case 36306:case 35682:return u1;case 35679:case 36299:case 36307:return d1;case 35680:case 36300:case 36308:case 36293:return f1;case 36289:case 36303:case 36311:case 36292:return p1}}var wu=class{constructor(e,t,n){this.id=e,this.addr=n,this.cache=[],this.type=t.type,this.setValue=Y_(t.type)}},Au=class{constructor(e,t,n){this.id=e,this.addr=n,this.cache=[],this.type=t.type,this.size=t.size,this.setValue=m1(t.type)}},Ru=class{constructor(e){this.id=e,this.seq=[],this.map={}}setValue(e,t,n){let s=this.seq;for(let r=0,a=s.length;r!==a;++r){let o=s[r];o.setValue(e,t[o.id],n)}}},Su=/(\w+)(\])?(\[|\.)?/g;function Fp(i,e){i.seq.push(e),i.map[e.id]=e}function g1(i,e,t){let n=i.name,s=n.length;for(Su.lastIndex=0;;){let r=Su.exec(n),a=Su.lastIndex,o=r[1],c=r[2]==="]",l=r[3];if(c&&(o=o|0),l===void 0||l==="["&&a+2===s){Fp(t,l===void 0?new wu(o,i,e):new Au(o,i,e));break}else{let u=t.map[o];u===void 0&&(u=new Ru(o),Fp(t,u)),t=u}}}var ea=class{constructor(e,t){this.seq=[],this.map={};let n=e.getProgramParameter(t,e.ACTIVE_UNIFORMS);for(let a=0;a<n;++a){let o=e.getActiveUniform(t,a),c=e.getUniformLocation(t,o.name);g1(o,c,this)}let s=[],r=[];for(let a of this.seq)a.type===e.SAMPLER_2D_SHADOW||a.type===e.SAMPLER_CUBE_SHADOW||a.type===e.SAMPLER_2D_ARRAY_SHADOW?s.push(a):r.push(a);s.length>0&&(this.seq=s.concat(r))}setValue(e,t,n,s){let r=this.map[t];r!==void 0&&r.setValue(e,n,s)}setOptional(e,t,n){let s=t[n];s!==void 0&&this.setValue(e,n,s)}static upload(e,t,n,s){for(let r=0,a=t.length;r!==a;++r){let o=t[r],c=n[o.id];c.needsUpdate!==!1&&o.setValue(e,c.value,s)}}static seqWithValue(e,t){let n=[];for(let s=0,r=e.length;s!==r;++s){let a=e[s];a.id in t&&n.push(a)}return n}};function Up(i,e,t){let n=i.createShader(e);return i.shaderSource(n,t),i.compileShader(n),n}var b1=37297,x1=0;function _1(i,e){let t=i.split(`
`),n=[],s=Math.max(e-6,0),r=Math.min(e+6,t.length);for(let a=s;a<r;a++){let o=a+1;n.push(`${o===e?">":" "} ${o}: ${t[a]}`)}return n.join(`
`)}var Op=new et;function y1(i){lt._getMatrix(Op,lt.workingColorSpace,i);let e=`mat3( ${Op.elements.map(t=>t.toFixed(4))} )`;switch(lt.getTransfer(i)){case Ea:return[e,"LinearTransferOETF"];case wt:return[e,"sRGBTransferOETF"];default:return Ve("WebGLProgram: Unsupported color space: ",i),[e,"LinearTransferOETF"]}}function Bp(i,e,t){let n=i.getShaderParameter(e,i.COMPILE_STATUS),r=(i.getShaderInfoLog(e)||"").trim();if(n&&r==="")return"";let a=/ERROR: 0:(\d+)/.exec(r);if(a){let o=parseInt(a[1]);return t.toUpperCase()+`

`+r+`

`+_1(i.getShaderSource(e),o)}else return r}function v1(i,e){let t=y1(e);return[`vec4 ${i}( vec4 value ) {`,`	return ${t[1]}( vec4( value.rgb * ${t[0]}, value.a ) );`,"}"].join(`
`)}var M1={[Zh]:"Linear",[$h]:"Reinhard",[Qh]:"Cineon",[eo]:"ACESFilmic",[tu]:"AgX",[nu]:"Neutral",[eu]:"Custom"};function S1(i,e){let t=M1[e];return t===void 0?(Ve("WebGLProgram: Unsupported toneMapping:",e),"vec3 "+i+"( vec3 color ) { return LinearToneMapping( color ); }"):"vec3 "+i+"( vec3 color ) { return "+t+"ToneMapping( color ); }"}var wl=new P;function E1(){lt.getLuminanceCoefficients(wl);let i=wl.x.toFixed(4),e=wl.y.toFixed(4),t=wl.z.toFixed(4);return["float luminance( const in vec3 rgb ) {",`	const vec3 weights = vec3( ${i}, ${e}, ${t} );`,"	return dot( weights, rgb );","}"].join(`
`)}function T1(i){return[i.extensionClipCullDistance?"#extension GL_ANGLE_clip_cull_distance : require":"",i.extensionMultiDraw?"#extension GL_ANGLE_multi_draw : require":""].filter(po).join(`
`)}function w1(i){let e=[];for(let t in i){let n=i[t];n!==!1&&e.push("#define "+t+" "+n)}return e.join(`
`)}function A1(i,e){let t={},n=i.getProgramParameter(e,i.ACTIVE_ATTRIBUTES);for(let s=0;s<n;s++){let r=i.getActiveAttrib(e,s),a=r.name,o=1;r.type===i.FLOAT_MAT2&&(o=2),r.type===i.FLOAT_MAT3&&(o=3),r.type===i.FLOAT_MAT4&&(o=4),t[a]={type:r.type,location:i.getAttribLocation(e,a),locationSize:o}}return t}function po(i){return i!==""}function kp(i,e){let t=e.numSpotLightShadows+e.numSpotLightMaps-e.numSpotLightShadowsWithMaps;return i.replace(/NUM_SUN_LIGHTS/g,e.numSunLights).replace(/NUM_DIR_LIGHTS/g,e.numDirLights).replace(/NUM_SPOT_LIGHTS/g,e.numSpotLights).replace(/NUM_SPOT_LIGHT_MAPS/g,e.numSpotLightMaps).replace(/NUM_SPOT_LIGHT_COORDS/g,t).replace(/NUM_RECT_AREA_LIGHTS/g,e.numRectAreaLights).replace(/NUM_POINT_LIGHTS/g,e.numPointLights).replace(/NUM_HEMI_LIGHTS/g,e.numHemiLights).replace(/NUM_SUN_LIGHT_SHADOWS/g,e.numSunLightShadows).replace(/NUM_DIR_LIGHT_SHADOWS/g,e.numDirLightShadows).replace(/NUM_SPOT_LIGHT_SHADOWS_WITH_MAPS/g,e.numSpotLightShadowsWithMaps).replace(/NUM_SPOT_LIGHT_SHADOWS/g,e.numSpotLightShadows).replace(/NUM_POINT_LIGHT_SHADOWS/g,e.numPointLightShadows)}function zp(i,e){return i.replace(/NUM_CLIPPING_PLANES/g,e.numClippingPlanes).replace(/UNION_CLIPPING_PLANES/g,e.numClippingPlanes-e.numClipIntersection)}var R1=/^[ \t]*#include +<([\w\d./]+)>/gm;function Cu(i){return i.replace(R1,P1)}var C1=new Map;function P1(i,e){let t=ot[e];if(t===void 0){let n=C1.get(e);if(n!==void 0)t=ot[n],Ve('WebGLRenderer: Shader chunk "%s" has been deprecated. Use "%s" instead.',e,n);else throw new Error("THREE.WebGLProgram: Can not resolve #include <"+e+">")}return Cu(t)}var I1=/#pragma unroll_loop_start\s+for\s*\(\s*int\s+i\s*=\s*(\d+)\s*;\s*i\s*<\s*(\d+)\s*;\s*i\s*\+\+\s*\)\s*{([\s\S]+?)}\s+#pragma unroll_loop_end/g;function Hp(i){return i.replace(I1,L1)}function L1(i,e,t,n){let s="";for(let r=parseInt(e);r<parseInt(t);r++)s+=n.replace(/\[\s*i\s*\]/g,"[ "+r+" ]").replace(/UNROLLED_LOOP_INDEX/g,r);return s}function Gp(i){let e=`precision ${i.precision} float;
	precision ${i.precision} int;
	precision ${i.precision} sampler2D;
	precision ${i.precision} samplerCube;
	precision ${i.precision} sampler3D;
	precision ${i.precision} sampler2DArray;
	precision ${i.precision} sampler2DShadow;
	precision ${i.precision} samplerCubeShadow;
	precision ${i.precision} sampler2DArrayShadow;
	precision ${i.precision} isampler2D;
	precision ${i.precision} isampler3D;
	precision ${i.precision} isamplerCube;
	precision ${i.precision} isampler2DArray;
	precision ${i.precision} usampler2D;
	precision ${i.precision} usampler3D;
	precision ${i.precision} usamplerCube;
	precision ${i.precision} usampler2DArray;
	`;return i.precision==="highp"?e+=`
#define HIGH_PRECISION`:i.precision==="mediump"?e+=`
#define MEDIUM_PRECISION`:i.precision==="lowp"&&(e+=`
#define LOW_PRECISION`),e}var D1={[Qa]:"SHADOWMAP_TYPE_PCF",[Xr]:"SHADOWMAP_TYPE_VSM"};function N1(i){return D1[i.shadowMapType]||"SHADOWMAP_TYPE_BASIC"}var F1={[Rs]:"ENVMAP_TYPE_CUBE",[tr]:"ENVMAP_TYPE_CUBE",[to]:"ENVMAP_TYPE_CUBE_UV"};function U1(i){return i.envMap===!1?"ENVMAP_TYPE_CUBE":F1[i.envMapMode]||"ENVMAP_TYPE_CUBE"}var O1={[tr]:"ENVMAP_MODE_REFRACTION"};function B1(i){return i.envMap===!1?"ENVMAP_MODE_REFLECTION":O1[i.envMapMode]||"ENVMAP_MODE_REFLECTION"}var k1={[Uc]:"ENVMAP_BLENDING_MULTIPLY",[tp]:"ENVMAP_BLENDING_MIX",[np]:"ENVMAP_BLENDING_ADD"};function z1(i){return i.envMap===!1?"ENVMAP_BLENDING_NONE":k1[i.combine]||"ENVMAP_BLENDING_NONE"}function H1(i){let e=i.envMapCubeUVHeight;if(e===null)return null;let t=Math.log2(e)-2,n=1/e;return{texelWidth:1/(3*Math.max(Math.pow(2,t),112)),texelHeight:n,maxMip:t}}function G1(i,e,t,n){let s=i.getContext(),r=t.defines,a=t.vertexShader,o=t.fragmentShader,c=N1(t),l=U1(t),h=B1(t),u=z1(t),d=H1(t),f=T1(t),m=w1(r),b=s.createProgram(),g,p,_=t.glslVersion?"#version "+t.glslVersion+`
`:"";t.isRawShaderMaterial?(g=["#define SHADER_TYPE "+t.shaderType,"#define SHADER_NAME "+t.shaderName,m].filter(po).join(`
`),g.length>0&&(g+=`
`),p=["#define SHADER_TYPE "+t.shaderType,"#define SHADER_NAME "+t.shaderName,m].filter(po).join(`
`),p.length>0&&(p+=`
`)):(g=[Gp(t),"#define SHADER_TYPE "+t.shaderType,"#define SHADER_NAME "+t.shaderName,m,t.extensionClipCullDistance?"#define USE_CLIP_DISTANCE":"",t.batching?"#define USE_BATCHING":"",t.batchingColor?"#define USE_BATCHING_COLOR":"",t.instancing?"#define USE_INSTANCING":"",t.instancingColor?"#define USE_INSTANCING_COLOR":"",t.instancingMorph?"#define USE_INSTANCING_MORPH":"",t.useFog&&t.fog?"#define USE_FOG":"",t.useFog&&t.fogExp2?"#define FOG_EXP2":"",t.map?"#define USE_MAP":"",t.envMap?"#define USE_ENVMAP":"",t.envMap?"#define "+h:"",t.lightMap?"#define USE_LIGHTMAP":"",t.aoMap?"#define USE_AOMAP":"",t.bumpMap?"#define USE_BUMPMAP":"",t.normalMap?"#define USE_NORMALMAP":"",t.normalMapObjectSpace?"#define USE_NORMALMAP_OBJECTSPACE":"",t.normalMapTangentSpace?"#define USE_NORMALMAP_TANGENTSPACE":"",t.displacementMap?"#define USE_DISPLACEMENTMAP":"",t.emissiveMap?"#define USE_EMISSIVEMAP":"",t.anisotropy?"#define USE_ANISOTROPY":"",t.anisotropyMap?"#define USE_ANISOTROPYMAP":"",t.clearcoatMap?"#define USE_CLEARCOATMAP":"",t.clearcoatRoughnessMap?"#define USE_CLEARCOAT_ROUGHNESSMAP":"",t.clearcoatNormalMap?"#define USE_CLEARCOAT_NORMALMAP":"",t.iridescenceMap?"#define USE_IRIDESCENCEMAP":"",t.iridescenceThicknessMap?"#define USE_IRIDESCENCE_THICKNESSMAP":"",t.specularMap?"#define USE_SPECULARMAP":"",t.specularColorMap?"#define USE_SPECULAR_COLORMAP":"",t.specularIntensityMap?"#define USE_SPECULAR_INTENSITYMAP":"",t.roughnessMap?"#define USE_ROUGHNESSMAP":"",t.metalnessMap?"#define USE_METALNESSMAP":"",t.alphaMap?"#define USE_ALPHAMAP":"",t.alphaHash?"#define USE_ALPHAHASH":"",t.transmission?"#define USE_TRANSMISSION":"",t.transmissionMap?"#define USE_TRANSMISSIONMAP":"",t.thicknessMap?"#define USE_THICKNESSMAP":"",t.sheenColorMap?"#define USE_SHEEN_COLORMAP":"",t.sheenRoughnessMap?"#define USE_SHEEN_ROUGHNESSMAP":"",t.mapUv?"#define MAP_UV "+t.mapUv:"",t.alphaMapUv?"#define ALPHAMAP_UV "+t.alphaMapUv:"",t.lightMapUv?"#define LIGHTMAP_UV "+t.lightMapUv:"",t.aoMapUv?"#define AOMAP_UV "+t.aoMapUv:"",t.emissiveMapUv?"#define EMISSIVEMAP_UV "+t.emissiveMapUv:"",t.bumpMapUv?"#define BUMPMAP_UV "+t.bumpMapUv:"",t.normalMapUv?"#define NORMALMAP_UV "+t.normalMapUv:"",t.displacementMapUv?"#define DISPLACEMENTMAP_UV "+t.displacementMapUv:"",t.metalnessMapUv?"#define METALNESSMAP_UV "+t.metalnessMapUv:"",t.roughnessMapUv?"#define ROUGHNESSMAP_UV "+t.roughnessMapUv:"",t.anisotropyMapUv?"#define ANISOTROPYMAP_UV "+t.anisotropyMapUv:"",t.clearcoatMapUv?"#define CLEARCOATMAP_UV "+t.clearcoatMapUv:"",t.clearcoatNormalMapUv?"#define CLEARCOAT_NORMALMAP_UV "+t.clearcoatNormalMapUv:"",t.clearcoatRoughnessMapUv?"#define CLEARCOAT_ROUGHNESSMAP_UV "+t.clearcoatRoughnessMapUv:"",t.iridescenceMapUv?"#define IRIDESCENCEMAP_UV "+t.iridescenceMapUv:"",t.iridescenceThicknessMapUv?"#define IRIDESCENCE_THICKNESSMAP_UV "+t.iridescenceThicknessMapUv:"",t.sheenColorMapUv?"#define SHEEN_COLORMAP_UV "+t.sheenColorMapUv:"",t.sheenRoughnessMapUv?"#define SHEEN_ROUGHNESSMAP_UV "+t.sheenRoughnessMapUv:"",t.specularMapUv?"#define SPECULARMAP_UV "+t.specularMapUv:"",t.specularColorMapUv?"#define SPECULAR_COLORMAP_UV "+t.specularColorMapUv:"",t.specularIntensityMapUv?"#define SPECULAR_INTENSITYMAP_UV "+t.specularIntensityMapUv:"",t.transmissionMapUv?"#define TRANSMISSIONMAP_UV "+t.transmissionMapUv:"",t.thicknessMapUv?"#define THICKNESSMAP_UV "+t.thicknessMapUv:"",t.vertexTangents&&t.flatShading===!1?"#define USE_TANGENT":"",t.vertexNormals?"#define HAS_NORMAL":"",t.vertexColors?"#define USE_COLOR":"",t.vertexAlphas?"#define USE_COLOR_ALPHA":"",t.vertexUv1s?"#define USE_UV1":"",t.vertexUv2s?"#define USE_UV2":"",t.vertexUv3s?"#define USE_UV3":"",t.pointsUvs?"#define USE_POINTS_UV":"",t.flatShading?"#define FLAT_SHADED":"",t.skinning?"#define USE_SKINNING":"",t.morphTargets?"#define USE_MORPHTARGETS":"",t.morphNormals&&t.flatShading===!1?"#define USE_MORPHNORMALS":"",t.morphColors?"#define USE_MORPHCOLORS":"",t.morphTargetsCount>0?"#define MORPHTARGETS_TEXTURE_STRIDE "+t.morphTextureStride:"",t.morphTargetsCount>0?"#define MORPHTARGETS_COUNT "+t.morphTargetsCount:"",t.doubleSided?"#define DOUBLE_SIDED":"",t.flipSided?"#define FLIP_SIDED":"",t.shadowMapEnabled?"#define USE_SHADOWMAP":"",t.shadowMapEnabled?"#define "+c:"",t.sizeAttenuation?"#define USE_SIZEATTENUATION":"",t.numLightProbes>0?"#define USE_LIGHT_PROBES":"",t.logarithmicDepthBuffer?"#define USE_LOGARITHMIC_DEPTH_BUFFER":"",t.reversedDepthBuffer?"#define USE_REVERSED_DEPTH_BUFFER":"","uniform mat4 modelMatrix;","uniform mat4 modelViewMatrix;","uniform mat4 projectionMatrix;","uniform mat4 viewMatrix;","uniform mat3 normalMatrix;","uniform vec3 cameraPosition;","uniform bool isOrthographic;","#ifdef USE_INSTANCING","	attribute mat4 instanceMatrix;","#endif","#ifdef USE_INSTANCING_COLOR","	attribute vec3 instanceColor;","#endif","#ifdef USE_INSTANCING_MORPH","	uniform sampler2D morphTexture;","#endif","attribute vec3 position;","attribute vec3 normal;","attribute vec2 uv;","#ifdef USE_UV1","	attribute vec2 uv1;","#endif","#ifdef USE_UV2","	attribute vec2 uv2;","#endif","#ifdef USE_UV3","	attribute vec2 uv3;","#endif","#ifdef USE_TANGENT","	attribute vec4 tangent;","#endif","#if defined( USE_COLOR_ALPHA )","	attribute vec4 color;","#elif defined( USE_COLOR )","	attribute vec3 color;","#endif","#ifdef USE_SKINNING","	attribute vec4 skinIndex;","	attribute vec4 skinWeight;","#endif",`
`].filter(po).join(`
`),p=[Gp(t),"#define SHADER_TYPE "+t.shaderType,"#define SHADER_NAME "+t.shaderName,m,t.useFog&&t.fog?"#define USE_FOG":"",t.useFog&&t.fogExp2?"#define FOG_EXP2":"",t.alphaToCoverage?"#define ALPHA_TO_COVERAGE":"",t.map?"#define USE_MAP":"",t.matcap?"#define USE_MATCAP":"",t.envMap?"#define USE_ENVMAP":"",t.envMap?"#define "+l:"",t.envMap?"#define "+h:"",t.envMap?"#define "+u:"",d?"#define CUBEUV_TEXEL_WIDTH "+d.texelWidth:"",d?"#define CUBEUV_TEXEL_HEIGHT "+d.texelHeight:"",d?"#define CUBEUV_MAX_MIP "+d.maxMip+".0":"",t.lightMap?"#define USE_LIGHTMAP":"",t.aoMap?"#define USE_AOMAP":"",t.bumpMap?"#define USE_BUMPMAP":"",t.normalMap?"#define USE_NORMALMAP":"",t.normalMapObjectSpace?"#define USE_NORMALMAP_OBJECTSPACE":"",t.normalMapTangentSpace?"#define USE_NORMALMAP_TANGENTSPACE":"",t.packedNormalMap?"#define USE_PACKED_NORMALMAP":"",t.emissiveMap?"#define USE_EMISSIVEMAP":"",t.anisotropy?"#define USE_ANISOTROPY":"",t.anisotropyMap?"#define USE_ANISOTROPYMAP":"",t.clearcoat?"#define USE_CLEARCOAT":"",t.clearcoatMap?"#define USE_CLEARCOATMAP":"",t.clearcoatRoughnessMap?"#define USE_CLEARCOAT_ROUGHNESSMAP":"",t.clearcoatNormalMap?"#define USE_CLEARCOAT_NORMALMAP":"",t.dispersion?"#define USE_DISPERSION":"",t.retroreflection?"#define USE_RETROREFLECTION":"",t.iridescence?"#define USE_IRIDESCENCE":"",t.iridescenceMap?"#define USE_IRIDESCENCEMAP":"",t.iridescenceThicknessMap?"#define USE_IRIDESCENCE_THICKNESSMAP":"",t.specularMap?"#define USE_SPECULARMAP":"",t.specularColorMap?"#define USE_SPECULAR_COLORMAP":"",t.specularIntensityMap?"#define USE_SPECULAR_INTENSITYMAP":"",t.roughnessMap?"#define USE_ROUGHNESSMAP":"",t.metalnessMap?"#define USE_METALNESSMAP":"",t.alphaMap?"#define USE_ALPHAMAP":"",t.alphaTest?"#define USE_ALPHATEST":"",t.alphaHash?"#define USE_ALPHAHASH":"",t.sheen?"#define USE_SHEEN":"",t.sheenColorMap?"#define USE_SHEEN_COLORMAP":"",t.sheenRoughnessMap?"#define USE_SHEEN_ROUGHNESSMAP":"",t.transmission?"#define USE_TRANSMISSION":"",t.transmissionMap?"#define USE_TRANSMISSIONMAP":"",t.thicknessMap?"#define USE_THICKNESSMAP":"",t.vertexTangents&&t.flatShading===!1?"#define USE_TANGENT":"",t.vertexColors||t.instancingColor?"#define USE_COLOR":"",t.vertexAlphas||t.batchingColor?"#define USE_COLOR_ALPHA":"",t.vertexUv1s?"#define USE_UV1":"",t.vertexUv2s?"#define USE_UV2":"",t.vertexUv3s?"#define USE_UV3":"",t.pointsUvs?"#define USE_POINTS_UV":"",t.gradientMap?"#define USE_GRADIENTMAP":"",t.flatShading?"#define FLAT_SHADED":"",t.doubleSided?"#define DOUBLE_SIDED":"",t.flipSided?"#define FLIP_SIDED":"",t.shadowMapEnabled?"#define USE_SHADOWMAP":"",t.shadowMapEnabled?"#define "+c:"",t.premultipliedAlpha?"#define PREMULTIPLIED_ALPHA":"",t.numLightProbes>0?"#define USE_LIGHT_PROBES":"",t.numLightProbeGrids>0?"#define USE_LIGHT_PROBES_GRID":"",t.decodeVideoTexture?"#define DECODE_VIDEO_TEXTURE":"",t.decodeVideoTextureEmissive?"#define DECODE_VIDEO_TEXTURE_EMISSIVE":"",t.logarithmicDepthBuffer?"#define USE_LOGARITHMIC_DEPTH_BUFFER":"",t.reversedDepthBuffer?"#define USE_REVERSED_DEPTH_BUFFER":"","uniform mat4 viewMatrix;","uniform vec3 cameraPosition;","uniform bool isOrthographic;",t.toneMapping!==vi?"#define TONE_MAPPING":"",t.toneMapping!==vi?ot.tonemapping_pars_fragment:"",t.toneMapping!==vi?S1("toneMapping",t.toneMapping):"",t.dithering?"#define DITHERING":"",t.opaque?"#define OPAQUE":"",ot.colorspace_pars_fragment,v1("linearToOutputTexel",t.outputColorSpace),E1(),t.useDepthPacking?"#define DEPTH_PACKING "+t.depthPacking:"",`
`].filter(po).join(`
`)),a=Cu(a),a=kp(a,t),a=zp(a,t),o=Cu(o),o=kp(o,t),o=zp(o,t),a=Hp(a),o=Hp(o),t.isRawShaderMaterial!==!0&&(_=`#version 300 es
`,g=[f,"#define attribute in","#define varying out","#define texture2D texture"].join(`
`)+`
`+g,p=["#define varying in",t.glslVersion===du?"":"layout(location = 0) out highp vec4 pc_fragColor;",t.glslVersion===du?"":"#define gl_FragColor pc_fragColor","#define gl_FragDepthEXT gl_FragDepth","#define texture2D texture","#define textureCube texture","#define texture2DProj textureProj","#define texture2DLodEXT textureLod","#define texture2DProjLodEXT textureProjLod","#define textureCubeLodEXT textureLod","#define texture2DGradEXT textureGrad","#define texture2DProjGradEXT textureProjGrad","#define textureCubeGradEXT textureGrad"].join(`
`)+`
`+p);let S=_+g+a,x=_+p+o,M=Up(s,s.VERTEX_SHADER,S),E=Up(s,s.FRAGMENT_SHADER,x);s.attachShader(b,M),s.attachShader(b,E),t.index0AttributeName!==void 0?s.bindAttribLocation(b,0,t.index0AttributeName):t.hasPositionAttribute===!0&&s.bindAttribLocation(b,0,"position"),s.linkProgram(b);function A(I){if(i.debug.checkShaderErrors){let L=s.getProgramInfoLog(b)||"",F=s.getShaderInfoLog(M)||"",D=s.getShaderInfoLog(E)||"",k=L.trim(),W=F.trim(),X=D.trim(),re=!0,j=!0;if(s.getProgramParameter(b,s.LINK_STATUS)===!1)if(re=!1,typeof i.debug.onShaderError=="function")i.debug.onShaderError(s,b,M,E);else{let ee=Bp(s,M,"vertex"),te=Bp(s,E,"fragment");Ye("WebGLProgram: Shader Error "+s.getError()+" - VALIDATE_STATUS "+s.getProgramParameter(b,s.VALIDATE_STATUS)+`

Material Name: `+I.name+`
Material Type: `+I.type+`

Program Info Log: `+k+`
`+ee+`
`+te)}else k!==""?Ve("WebGLProgram: Program Info Log:",k):(W===""||X==="")&&(j=!1);j&&(I.diagnostics={runnable:re,programLog:k,vertexShader:{log:W,prefix:g},fragmentShader:{log:X,prefix:p}})}s.deleteShader(M),s.deleteShader(E),y=new ea(s,b),w=A1(s,b)}let y;this.getUniforms=function(){return y===void 0&&A(this),y};let w;this.getAttributes=function(){return w===void 0&&A(this),w};let R=t.rendererExtensionParallelShaderCompile===!1;return this.isReady=function(){return R===!1&&(R=s.getProgramParameter(b,b1)),R},this.destroy=function(){n.releaseStatesOfProgram(this),s.deleteProgram(b),this.program=void 0},this.type=t.shaderType,this.name=t.shaderName,this.id=x1++,this.cacheKey=e,this.usedTimes=1,this.program=b,this.vertexShader=M,this.fragmentShader=E,this}var V1=0,Pu=class{constructor(){this.shaderCache=new Map,this.materialCache=new Map}update(e,t,n){let s=this._getShaderCacheForMaterial(e);return s.has(t)===!1&&(s.add(t),t.usedTimes++),s.has(n)===!1&&(s.add(n),n.usedTimes++),this}remove(e){let t=this.materialCache.get(e);for(let n of t)n.usedTimes--,n.usedTimes===0&&this.shaderCache.delete(n.code);return this.materialCache.delete(e),this}getVertexShaderStage(e){return this._getShaderStage(e.vertexShader)}getFragmentShaderStage(e){return this._getShaderStage(e.fragmentShader)}dispose(){this.shaderCache.clear(),this.materialCache.clear()}_getShaderCacheForMaterial(e){let t=this.materialCache,n=t.get(e);return n===void 0&&(n=new Set,t.set(e,n)),n}_getShaderStage(e){let t=this.shaderCache,n=t.get(e);return n===void 0&&(n=new Iu(e),t.set(e,n)),n}},Iu=class{constructor(e){this.id=V1++,this.code=e,this.usedTimes=0}};function W1(i){return i===Ps||i===ao||i===oo}function q1(i,e,t,n,s,r){let a=new Dr,o=new Pu,c=new Set,l=[],h=new Map,u=n.logarithmicDepthBuffer,d=n.precision,f={MeshDepthMaterial:"depth",MeshDistanceMaterial:"distance",MeshNormalMaterial:"normal",MeshBasicMaterial:"basic",MeshLambertMaterial:"lambert",MeshPhongMaterial:"phong",MeshToonMaterial:"toon",MeshStandardMaterial:"physical",MeshPhysicalMaterial:"physical",MeshMatcapMaterial:"matcap",LineBasicMaterial:"basic",LineDashedMaterial:"dashed",PointsMaterial:"points",ShadowMaterial:"shadow",SpriteMaterial:"sprite"};function m(y){return c.add(y),y===0?"uv":`uv${y}`}function b(y,w,R,I,L,F){let D=I.fog,k=L.geometry,W=y.isMeshStandardMaterial||y.isMeshLambertMaterial||y.isMeshPhongMaterial?I.environment:null,X=y.isMeshStandardMaterial||y.isMeshLambertMaterial&&!y.envMap||y.isMeshPhongMaterial&&!y.envMap,re=e.get(y.envMap||W,X),j=re&&re.mapping===to?re.image.height:null,ee=f[y.type];y.precision!==null&&(d=n.getMaxPrecision(y.precision),d!==y.precision&&Ve("WebGLProgram.getParameters:",y.precision,"not supported, using",d,"instead."));let te=k.morphAttributes.position||k.morphAttributes.normal||k.morphAttributes.color,Ne=te!==void 0?te.length:0,Se=0;k.morphAttributes.position!==void 0&&(Se=1),k.morphAttributes.normal!==void 0&&(Se=2),k.morphAttributes.color!==void 0&&(Se=3);let ct,$e,ht,J;if(ee){let kt=ki[ee];ct=kt.vertexShader,$e=kt.fragmentShader}else{ct=y.vertexShader,$e=y.fragmentShader;let kt=o.getVertexShaderStage(y),Et=o.getFragmentShaderStage(y);o.update(y,kt,Et),ht=kt.id,J=Et.id}let ie=i.getRenderTarget(),ve=i.state.buffers.depth.getReversed(),Ke=L.isInstancedMesh===!0,Ae=L.isBatchedMesh===!0,ze=!!y.map,bt=!!y.matcap,Q=!!re,ce=!!y.aoMap,le=!!y.lightMap,de=!!y.bumpMap&&y.wireframe===!1,xe=!!y.normalMap,Xe=!!y.displacementMap,Fe=!!y.emissiveMap,qe=!!y.metalnessMap,Je=!!y.roughnessMap,N=y.anisotropy>0,xt=y.clearcoat>0,st=y.dispersion>0,C=y.retroreflectivity>0,v=y.iridescence>0,z=y.sheen>0,V=y.transmission>0,Y=N&&!!y.anisotropyMap,pe=xt&&!!y.clearcoatMap,me=xt&&!!y.clearcoatNormalMap,Z=xt&&!!y.clearcoatRoughnessMap,ne=v&&!!y.iridescenceMap,ye=v&&!!y.iridescenceThicknessMap,He=z&&!!y.sheenColorMap,Me=z&&!!y.sheenRoughnessMap,q=!!y.specularMap,oe=!!y.specularColorMap,ge=!!y.specularIntensityMap,je=V&&!!y.transmissionMap,U=V&&!!y.thicknessMap,fe=!!y.gradientMap,B=!!y.alphaMap,ue=y.alphaTest>0,be=!!y.alphaHash,se=!!y.extensions,Ie=vi;y.toneMapped&&(ie===null||ie.isXRRenderTarget===!0)&&(Ie=i.toneMapping);let Pe={shaderID:ee,shaderType:y.type,shaderName:y.name,vertexShader:ct,fragmentShader:$e,defines:y.defines,customVertexShaderID:ht,customFragmentShaderID:J,isRawShaderMaterial:y.isRawShaderMaterial===!0,glslVersion:y.glslVersion,precision:d,batching:Ae,batchingColor:Ae&&L._colorsTexture!==null,instancing:Ke,instancingColor:Ke&&L.instanceColor!==null,instancingMorph:Ke&&L.morphTexture!==null,outputColorSpace:ie===null?i.outputColorSpace:ie.isXRRenderTarget===!0?ie.texture.colorSpace:lt.workingColorSpace,alphaToCoverage:!!y.alphaToCoverage,map:ze,matcap:bt,envMap:Q,envMapMode:Q&&re.mapping,envMapCubeUVHeight:j,aoMap:ce,lightMap:le,bumpMap:de,normalMap:xe,displacementMap:Xe,emissiveMap:Fe,normalMapObjectSpace:xe&&y.normalMapType===ap,normalMapTangentSpace:xe&&y.normalMapType===lo,packedNormalMap:xe&&y.normalMapType===lo&&W1(y.normalMap.format),metalnessMap:qe,roughnessMap:Je,anisotropy:N,anisotropyMap:Y,clearcoat:xt,clearcoatMap:pe,clearcoatNormalMap:me,clearcoatRoughnessMap:Z,dispersion:st,retroreflection:C,iridescence:v,iridescenceMap:ne,iridescenceThicknessMap:ye,sheen:z,sheenColorMap:He,sheenRoughnessMap:Me,specularMap:q,specularColorMap:oe,specularIntensityMap:ge,transmission:V,transmissionMap:je,thicknessMap:U,gradientMap:fe,opaque:y.transparent===!1&&y.blending===jr&&y.alphaToCoverage===!1,alphaMap:B,alphaTest:ue,alphaHash:be,combine:y.combine,mapUv:ze&&m(y.map.channel),aoMapUv:ce&&m(y.aoMap.channel),lightMapUv:le&&m(y.lightMap.channel),bumpMapUv:de&&m(y.bumpMap.channel),normalMapUv:xe&&m(y.normalMap.channel),displacementMapUv:Xe&&m(y.displacementMap.channel),emissiveMapUv:Fe&&m(y.emissiveMap.channel),metalnessMapUv:qe&&m(y.metalnessMap.channel),roughnessMapUv:Je&&m(y.roughnessMap.channel),anisotropyMapUv:Y&&m(y.anisotropyMap.channel),clearcoatMapUv:pe&&m(y.clearcoatMap.channel),clearcoatNormalMapUv:me&&m(y.clearcoatNormalMap.channel),clearcoatRoughnessMapUv:Z&&m(y.clearcoatRoughnessMap.channel),iridescenceMapUv:ne&&m(y.iridescenceMap.channel),iridescenceThicknessMapUv:ye&&m(y.iridescenceThicknessMap.channel),sheenColorMapUv:He&&m(y.sheenColorMap.channel),sheenRoughnessMapUv:Me&&m(y.sheenRoughnessMap.channel),specularMapUv:q&&m(y.specularMap.channel),specularColorMapUv:oe&&m(y.specularColorMap.channel),specularIntensityMapUv:ge&&m(y.specularIntensityMap.channel),transmissionMapUv:je&&m(y.transmissionMap.channel),thicknessMapUv:U&&m(y.thicknessMap.channel),alphaMapUv:B&&m(y.alphaMap.channel),vertexTangents:!!k.attributes.tangent&&(xe||N),vertexNormals:!!k.attributes.normal,vertexColors:y.vertexColors,vertexAlphas:y.vertexColors===!0&&!!k.attributes.color&&k.attributes.color.itemSize===4,pointsUvs:L.isPoints===!0&&!!k.attributes.uv&&(ze||B),fog:!!D,useFog:y.fog===!0,fogExp2:!!D&&D.isFogExp2,flatShading:y.wireframe===!1&&(y.flatShading===!0||k.attributes.normal===void 0&&xe===!1&&(y.isMeshLambertMaterial||y.isMeshPhongMaterial||y.isMeshStandardMaterial||y.isMeshPhysicalMaterial)),sizeAttenuation:y.sizeAttenuation===!0,logarithmicDepthBuffer:u,reversedDepthBuffer:ve,skinning:L.isSkinnedMesh===!0,hasPositionAttribute:k.attributes.position!==void 0,morphTargets:k.morphAttributes.position!==void 0,morphNormals:k.morphAttributes.normal!==void 0,morphColors:k.morphAttributes.color!==void 0,morphTargetsCount:Ne,morphTextureStride:Se,numSunLights:w.sun.length,numDirLights:w.directional.length,numPointLights:w.point.length,numSpotLights:w.spot.length,numSpotLightMaps:w.spotLightMap.length,numRectAreaLights:w.rectArea.length,numHemiLights:w.hemi.length,numSunLightShadows:w.sunShadowMap.length,numDirLightShadows:w.directionalShadowMap.length,numPointLightShadows:w.pointShadowMap.length,numSpotLightShadows:w.spotShadowMap.length,numSpotLightShadowsWithMaps:w.numSpotLightShadowsWithMaps,numLightProbes:w.numLightProbes,numLightProbeGrids:F.length,numClippingPlanes:r.numPlanes,numClipIntersection:r.numIntersection,dithering:y.dithering,shadowMapEnabled:i.shadowMap.enabled&&R.length>0,shadowMapType:i.shadowMap.type,toneMapping:Ie,decodeVideoTexture:ze&&y.map.isVideoTexture===!0&&lt.getTransfer(y.map.colorSpace)===wt,decodeVideoTextureEmissive:Fe&&y.emissiveMap.isVideoTexture===!0&&lt.getTransfer(y.emissiveMap.colorSpace)===wt,premultipliedAlpha:y.premultipliedAlpha,doubleSided:y.side===oi,flipSided:y.side===yn,useDepthPacking:y.depthPacking>=0,depthPacking:y.depthPacking||0,index0AttributeName:y.index0AttributeName,extensionClipCullDistance:se&&y.extensions.clipCullDistance===!0&&t.has("WEBGL_clip_cull_distance"),extensionMultiDraw:(se&&y.extensions.multiDraw===!0||Ae)&&t.has("WEBGL_multi_draw"),rendererExtensionParallelShaderCompile:t.has("KHR_parallel_shader_compile"),customProgramCacheKey:y.customProgramCacheKey()};return Pe.vertexUv1s=c.has(1),Pe.vertexUv2s=c.has(2),Pe.vertexUv3s=c.has(3),c.clear(),Pe}function g(y){let w=[];if(y.shaderID?w.push(y.shaderID):(w.push(y.customVertexShaderID),w.push(y.customFragmentShaderID)),y.defines!==void 0)for(let R in y.defines)w.push(R),w.push(y.defines[R]);return y.isRawShaderMaterial===!1&&(p(w,y),_(w,y),w.push(i.outputColorSpace)),w.push(y.customProgramCacheKey),w.join()}function p(y,w){y.push(w.precision),y.push(w.outputColorSpace),y.push(w.envMapMode),y.push(w.envMapCubeUVHeight),y.push(w.mapUv),y.push(w.alphaMapUv),y.push(w.lightMapUv),y.push(w.aoMapUv),y.push(w.bumpMapUv),y.push(w.normalMapUv),y.push(w.displacementMapUv),y.push(w.emissiveMapUv),y.push(w.metalnessMapUv),y.push(w.roughnessMapUv),y.push(w.anisotropyMapUv),y.push(w.clearcoatMapUv),y.push(w.clearcoatNormalMapUv),y.push(w.clearcoatRoughnessMapUv),y.push(w.iridescenceMapUv),y.push(w.iridescenceThicknessMapUv),y.push(w.sheenColorMapUv),y.push(w.sheenRoughnessMapUv),y.push(w.specularMapUv),y.push(w.specularColorMapUv),y.push(w.specularIntensityMapUv),y.push(w.transmissionMapUv),y.push(w.thicknessMapUv),y.push(w.combine),y.push(w.fogExp2),y.push(w.sizeAttenuation),y.push(w.morphTargetsCount),y.push(w.morphAttributeCount),y.push(w.numSunLights),y.push(w.numDirLights),y.push(w.numPointLights),y.push(w.numSpotLights),y.push(w.numSpotLightMaps),y.push(w.numHemiLights),y.push(w.numRectAreaLights),y.push(w.numSunLightShadows),y.push(w.numDirLightShadows),y.push(w.numPointLightShadows),y.push(w.numSpotLightShadows),y.push(w.numSpotLightShadowsWithMaps),y.push(w.numLightProbes),y.push(w.shadowMapType),y.push(w.toneMapping),y.push(w.numClippingPlanes),y.push(w.numClipIntersection),y.push(w.depthPacking)}function _(y,w){a.disableAll(),w.instancing&&a.enable(0),w.instancingColor&&a.enable(1),w.instancingMorph&&a.enable(2),w.matcap&&a.enable(3),w.envMap&&a.enable(4),w.normalMapObjectSpace&&a.enable(5),w.normalMapTangentSpace&&a.enable(6),w.clearcoat&&a.enable(7),w.iridescence&&a.enable(8),w.alphaTest&&a.enable(9),w.vertexColors&&a.enable(10),w.vertexAlphas&&a.enable(11),w.vertexUv1s&&a.enable(12),w.vertexUv2s&&a.enable(13),w.vertexUv3s&&a.enable(14),w.vertexTangents&&a.enable(15),w.anisotropy&&a.enable(16),w.alphaHash&&a.enable(17),w.batching&&a.enable(18),w.dispersion&&a.enable(19),w.retroreflection&&a.enable(24),w.batchingColor&&a.enable(20),w.gradientMap&&a.enable(21),w.packedNormalMap&&a.enable(22),w.vertexNormals&&a.enable(23),y.push(a.mask),a.disableAll(),w.fog&&a.enable(0),w.useFog&&a.enable(1),w.flatShading&&a.enable(2),w.logarithmicDepthBuffer&&a.enable(3),w.reversedDepthBuffer&&a.enable(4),w.skinning&&a.enable(5),w.morphTargets&&a.enable(6),w.morphNormals&&a.enable(7),w.morphColors&&a.enable(8),w.premultipliedAlpha&&a.enable(9),w.shadowMapEnabled&&a.enable(10),w.doubleSided&&a.enable(11),w.flipSided&&a.enable(12),w.useDepthPacking&&a.enable(13),w.dithering&&a.enable(14),w.transmission&&a.enable(15),w.sheen&&a.enable(16),w.opaque&&a.enable(17),w.pointsUvs&&a.enable(18),w.decodeVideoTexture&&a.enable(19),w.decodeVideoTextureEmissive&&a.enable(20),w.alphaToCoverage&&a.enable(21),w.numLightProbeGrids>0&&a.enable(22),w.hasPositionAttribute&&a.enable(23),y.push(a.mask)}function S(y){let w=f[y.type],R;if(w){let I=ki[w];R=Sp.clone(I.uniforms)}else R=y.uniforms;return R}function x(y,w){let R=h.get(w);return R!==void 0?++R.usedTimes:(R=new G1(i,w,y,s),l.push(R),h.set(w,R)),R}function M(y){if(--y.usedTimes===0){let w=l.indexOf(y);l[w]=l[l.length-1],l.pop(),h.delete(y.cacheKey),y.destroy()}}function E(y){o.remove(y)}function A(){o.dispose()}return{getParameters:b,getProgramCacheKey:g,getUniforms:S,acquireProgram:x,releaseProgram:M,releaseShaderCache:E,programs:l,dispose:A}}function X1(){let i=new WeakMap;function e(a){return i.has(a)}function t(a){let o=i.get(a);return o===void 0&&(o={},i.set(a,o)),o}function n(a){i.delete(a)}function s(a,o,c){i.get(a)[o]=c}function r(){i=new WeakMap}return{has:e,get:t,remove:n,update:s,dispose:r}}function j1(i,e){return i.groupOrder!==e.groupOrder?i.groupOrder-e.groupOrder:i.renderOrder!==e.renderOrder?i.renderOrder-e.renderOrder:i.material.id!==e.material.id?i.material.id-e.material.id:i.materialVariant!==e.materialVariant?i.materialVariant-e.materialVariant:i.z!==e.z?i.z-e.z:i.id-e.id}function Vp(i,e){return i.groupOrder!==e.groupOrder?i.groupOrder-e.groupOrder:i.renderOrder!==e.renderOrder?i.renderOrder-e.renderOrder:i.z!==e.z?e.z-i.z:i.id-e.id}function Wp(){let i=[],e=0,t=[],n=[],s=[];function r(){e=0,t.length=0,n.length=0,s.length=0}function a(d){let f=0;return d.isInstancedMesh&&(f+=2),d.isSkinnedMesh&&(f+=1),f}function o(d,f,m,b,g,p){let _=i[e];return _===void 0?(_={id:d.id,object:d,geometry:f,material:m,materialVariant:a(d),groupOrder:b,renderOrder:d.renderOrder,z:g,group:p},i[e]=_):(_.id=d.id,_.object=d,_.geometry=f,_.material=m,_.materialVariant=a(d),_.groupOrder=b,_.renderOrder=d.renderOrder,_.z=g,_.group=p),e++,_}function c(d,f,m,b,g,p,_){_.reversedDepth===!0&&(g=-g);let S=o(d,f,m,b,g,p);m.transmission>0?n.push(S):m.transparent===!0?s.push(S):t.push(S)}function l(d,f,m,b,g,p){let _=o(d,f,m,b,g,p);m.transmission>0?n.unshift(_):m.transparent===!0?s.unshift(_):t.unshift(_)}function h(d,f){t.length>1&&t.sort(d||j1),n.length>1&&n.sort(f||Vp),s.length>1&&s.sort(f||Vp)}function u(){for(let d=e,f=i.length;d<f;d++){let m=i[d];if(m.id===null)break;m.id=null,m.object=null,m.geometry=null,m.material=null,m.group=null}}return{opaque:t,transmissive:n,transparent:s,init:r,push:c,unshift:l,finish:u,sort:h}}function K1(){let i=new WeakMap;function e(n,s){let r=i.get(n),a;return r===void 0?(a=new Wp,i.set(n,[a])):s>=r.length?(a=new Wp,r.push(a)):a=r[s],a}function t(){i=new WeakMap}return{get:e,dispose:t}}function Y1(){let i={};return{get:function(e){if(i[e.id]!==void 0)return i[e.id];let t;switch(e.type){case"SunLight":case"DirectionalLight":t={direction:new P,color:new Be};break;case"SpotLight":t={position:new P,direction:new P,color:new Be,distance:0,coneCos:0,penumbraCos:0,decay:0};break;case"PointLight":t={position:new P,color:new Be,distance:0,decay:0};break;case"HemisphereLight":t={direction:new P,skyColor:new Be,groundColor:new Be};break;case"RectAreaLight":t={color:new Be,position:new P,halfWidth:new P,halfHeight:new P};break}return i[e.id]=t,t}}}function J1(){let i={};return{get:function(e){if(i[e.id]!==void 0)return i[e.id];let t;switch(e.type){case"SunLight":case"DirectionalLight":t={shadowIntensity:1,shadowBias:0,shadowNormalBias:0,shadowRadius:1,shadowMapSize:new ae};break;case"SpotLight":t={shadowIntensity:1,shadowBias:0,shadowNormalBias:0,shadowRadius:1,shadowMapSize:new ae};break;case"PointLight":t={shadowIntensity:1,shadowBias:0,shadowNormalBias:0,shadowRadius:1,shadowMapSize:new ae,shadowCameraNear:1,shadowCameraFar:1e3};break}return i[e.id]=t,t}}}var Z1=0;function $1(i,e){return(e.castShadow?2:0)-(i.castShadow?2:0)+(e.map?1:0)-(i.map?1:0)}function Q1(i){let e=new Y1,t=J1(),n={version:0,hash:{sunLength:-1,directionalLength:-1,pointLength:-1,spotLength:-1,rectAreaLength:-1,hemiLength:-1,numSunShadows:-1,numDirectionalShadows:-1,numPointShadows:-1,numSpotShadows:-1,numSpotMaps:-1,numLightProbes:-1},ambient:[0,0,0],probe:[],sun:[],sunShadow:[],sunShadowMap:[],sunShadowMatrix:[],sunShadowCascade:[],directional:[],directionalShadow:[],directionalShadowMap:[],directionalShadowMatrix:[],spot:[],spotLightMap:[],spotShadow:[],spotShadowMap:[],spotLightMatrix:[],rectArea:[],rectAreaLTC1:null,rectAreaLTC2:null,point:[],pointShadow:[],pointShadowMap:[],pointShadowMatrix:[],hemi:[],numSpotLightShadowsWithMaps:0,numLightProbes:0};for(let l=0;l<9;l++)n.probe.push(new P);let s=new P,r=new Ze,a=new Ze;function o(l){let h=0,u=0,d=0;for(let L=0;L<9;L++)n.probe[L].set(0,0,0);let f=0,m=0,b=0,g=0,p=0,_=0,S=0,x=0,M=0,E=0,A=0,y=0,w=0,R=0;l.sort($1);for(let L=0,F=l.length;L<F;L++){let D=l[L],k=D.color,W=D.intensity,X=D.distance,re=null;if(D.shadow&&D.shadow.map&&(D.shadow.map.texture.format===Ps?re=D.shadow.map.texture:re=D.shadow.map.depthTexture||D.shadow.map.texture),D.isAmbientLight)h+=k.r*W,u+=k.g*W,d+=k.b*W;else if(D.isLightProbe){for(let j=0;j<9;j++)n.probe[j].addScaledVector(D.sh.coefficients[j],W);R++}else if(D.isSunLight){let j=e.get(D);if(j.color.copy(D.color).multiplyScalar(D.intensity),D.castShadow){let ee=D.shadow,te=t.get(D);te.shadowIntensity=ee.intensity,te.shadowBias=ee.bias,te.shadowNormalBias=ee.normalBias,te.shadowRadius=ee.radius,te.shadowMapSize.copy(ee.mapSize).multiply(ee.getFrameExtents()),n.sunShadow[m]=te,n.sunShadowMap[m]=re;let Ne=ee.getViewportCount();for(let Se=0;Se<Ne;Se++)n.sunShadowMatrix[b+Se]=ee.getMatrix(Se),n.sunShadowCascade[b+Se]=ee._cascadeData[Se];b+=Ne,m++}n.sun[f]=j,f++}else if(D.isDirectionalLight){let j=e.get(D);if(j.color.copy(D.color).multiplyScalar(D.intensity),D.castShadow){let ee=D.shadow,te=t.get(D);te.shadowIntensity=ee.intensity,te.shadowBias=ee.bias,te.shadowNormalBias=ee.normalBias,te.shadowRadius=ee.radius,te.shadowMapSize=ee.mapSize,n.directionalShadow[g]=te,n.directionalShadowMap[g]=re,n.directionalShadowMatrix[g]=D.shadow.matrix,M++}n.directional[g]=j,g++}else if(D.isSpotLight){let j=e.get(D);j.position.setFromMatrixPosition(D.matrixWorld),j.color.copy(k).multiplyScalar(W),j.distance=X,j.coneCos=Math.cos(D.angle),j.penumbraCos=Math.cos(D.angle*(1-D.penumbra)),j.decay=D.decay,n.spot[_]=j;let ee=D.shadow;if(D.map&&(n.spotLightMap[y]=D.map,y++,ee.updateMatrices(D),D.castShadow&&w++),n.spotLightMatrix[_]=ee.matrix,D.castShadow){let te=t.get(D);te.shadowIntensity=ee.intensity,te.shadowBias=ee.bias,te.shadowNormalBias=ee.normalBias,te.shadowRadius=ee.radius,te.shadowMapSize=ee.mapSize,n.spotShadow[_]=te,n.spotShadowMap[_]=re,A++}_++}else if(D.isRectAreaLight){let j=e.get(D);j.color.copy(k).multiplyScalar(W),j.halfWidth.set(D.width*.5,0,0),j.halfHeight.set(0,D.height*.5,0),n.rectArea[S]=j,S++}else if(D.isPointLight){let j=e.get(D);if(j.color.copy(D.color).multiplyScalar(D.intensity),j.distance=D.distance,j.decay=D.decay,D.castShadow){let ee=D.shadow,te=t.get(D);te.shadowIntensity=ee.intensity,te.shadowBias=ee.bias,te.shadowNormalBias=ee.normalBias,te.shadowRadius=ee.radius,te.shadowMapSize=ee.mapSize,te.shadowCameraNear=ee.camera.near,te.shadowCameraFar=ee.camera.far,n.pointShadow[p]=te,n.pointShadowMap[p]=re,n.pointShadowMatrix[p]=D.shadow.matrix,E++}n.point[p]=j,p++}else if(D.isHemisphereLight){let j=e.get(D);j.skyColor.copy(D.color).multiplyScalar(W),j.groundColor.copy(D.groundColor).multiplyScalar(W),n.hemi[x]=j,x++}}S>0&&(i.has("OES_texture_float_linear")===!0?(n.rectAreaLTC1=Ee.LTC_FLOAT_1,n.rectAreaLTC2=Ee.LTC_FLOAT_2):(n.rectAreaLTC1=Ee.LTC_HALF_1,n.rectAreaLTC2=Ee.LTC_HALF_2)),n.ambient[0]=h,n.ambient[1]=u,n.ambient[2]=d;let I=n.hash;(I.sunLength!==f||I.directionalLength!==g||I.pointLength!==p||I.spotLength!==_||I.rectAreaLength!==S||I.hemiLength!==x||I.numSunShadows!==m||I.numDirectionalShadows!==M||I.numPointShadows!==E||I.numSpotShadows!==A||I.numSpotMaps!==y||I.numLightProbes!==R)&&(n.sun.length=f,n.directional.length=g,n.spot.length=_,n.rectArea.length=S,n.point.length=p,n.hemi.length=x,n.sunShadow.length=m,n.sunShadowMap.length=m,n.sunShadowMatrix.length=b,n.sunShadowCascade.length=b,n.directionalShadow.length=M,n.directionalShadowMap.length=M,n.directionalShadowMatrix.length=M,n.pointShadow.length=E,n.pointShadowMap.length=E,n.pointShadowMatrix.length=E,n.spotShadow.length=A,n.spotShadowMap.length=A,n.spotLightMatrix.length=A+y-w,n.spotLightMap.length=y,n.numSpotLightShadowsWithMaps=w,n.numLightProbes=R,I.sunLength=f,I.directionalLength=g,I.pointLength=p,I.spotLength=_,I.rectAreaLength=S,I.hemiLength=x,I.numSunShadows=m,I.numDirectionalShadows=M,I.numPointShadows=E,I.numSpotShadows=A,I.numSpotMaps=y,I.numLightProbes=R,n.version=Z1++)}function c(l,h){let u=0,d=0,f=0,m=0,b=0,g=0,p=h.matrixWorldInverse;for(let _=0,S=l.length;_<S;_++){let x=l[_];if(x.isSunLight){let M=n.sun[u];M.direction.setFromMatrixPosition(x.matrixWorld),M.direction.transformDirection(p),u++}else if(x.isDirectionalLight){let M=n.directional[d];M.direction.setFromMatrixPosition(x.matrixWorld),s.setFromMatrixPosition(x.target.matrixWorld),M.direction.sub(s),M.direction.transformDirection(p),d++}else if(x.isSpotLight){let M=n.spot[m];M.position.setFromMatrixPosition(x.matrixWorld),M.position.applyMatrix4(p),M.direction.setFromMatrixPosition(x.matrixWorld),s.setFromMatrixPosition(x.target.matrixWorld),M.direction.sub(s),M.direction.transformDirection(p),m++}else if(x.isRectAreaLight){let M=n.rectArea[b];M.position.setFromMatrixPosition(x.matrixWorld),M.position.applyMatrix4(p),a.identity(),r.copy(x.matrixWorld),r.premultiply(p),a.extractRotation(r),M.halfWidth.set(x.width*.5,0,0),M.halfHeight.set(0,x.height*.5,0),M.halfWidth.applyMatrix4(a),M.halfHeight.applyMatrix4(a),b++}else if(x.isPointLight){let M=n.point[f];M.position.setFromMatrixPosition(x.matrixWorld),M.position.applyMatrix4(p),f++}else if(x.isHemisphereLight){let M=n.hemi[g];M.direction.setFromMatrixPosition(x.matrixWorld),M.direction.transformDirection(p),g++}}}return{setup:o,setupView:c,state:n}}function qp(i){let e=new Q1(i),t=[],n=[],s=[];function r(d){u.camera=d,t.length=0,n.length=0,s.length=0}function a(d){t.push(d)}function o(d){n.push(d)}function c(d){s.push(d)}function l(){e.setup(t)}function h(d){e.setupView(t,d)}let u={lightsArray:t,shadowsArray:n,lightProbeGridArray:s,camera:null,lights:e,transmissionRenderTarget:{},textureUnits:0};return{init:r,state:u,setupLights:l,setupLightsView:h,pushLight:a,pushShadow:o,pushLightProbeGrid:c}}function ey(i){let e=new WeakMap;function t(s,r=0){let a=e.get(s),o;return a===void 0?(o=new qp(i),e.set(s,[o])):r>=a.length?(o=new qp(i),a.push(o)):o=a[r],o}function n(){e=new WeakMap}return{get:t,dispose:n}}var ty=`void main() {
	gl_Position = vec4( position, 1.0 );
}`,ny=`uniform sampler2D shadow_pass;
uniform vec2 resolution;
uniform float radius;
void main() {
	const float samples = float( VSM_SAMPLES );
	float mean = 0.0;
	float squared_mean = 0.0;
	float uvStride = samples <= 1.0 ? 0.0 : 2.0 / ( samples - 1.0 );
	float uvStart = samples <= 1.0 ? 0.0 : - 1.0;
	for ( float i = 0.0; i < samples; i ++ ) {
		float uvOffset = uvStart + i * uvStride;
		#ifdef HORIZONTAL_PASS
			vec2 distribution = texture2D( shadow_pass, ( gl_FragCoord.xy + vec2( uvOffset, 0.0 ) * radius ) / resolution ).rg;
			mean += distribution.x;
			squared_mean += distribution.y * distribution.y + distribution.x * distribution.x;
		#else
			float depth = texture2D( shadow_pass, ( gl_FragCoord.xy + vec2( 0.0, uvOffset ) * radius ) / resolution ).r;
			mean += depth;
			squared_mean += depth * depth;
		#endif
	}
	mean = mean / samples;
	squared_mean = squared_mean / samples;
	float std_dev = sqrt( max( 0.0, squared_mean - mean * mean ) );
	gl_FragColor = vec4( mean, std_dev, 0.0, 1.0 );
}`,iy=[new P(1,0,0),new P(-1,0,0),new P(0,1,0),new P(0,-1,0),new P(0,0,1),new P(0,0,-1)],sy=[new P(0,-1,0),new P(0,-1,0),new P(0,0,1),new P(0,0,-1),new P(0,-1,0),new P(0,-1,0)],Xp=new Ze,fo=new P,Eu=new P;function ry(i,e,t){let n=new kr,s=new ae,r=new ae,a=new Pt,o=new Ec,c=new Tc,l={},h=t.maxTextureSize,u={[Ui]:yn,[yn]:Ui,[oi]:oi},d=new Wn({defines:{VSM_SAMPLES:8},uniforms:{shadow_pass:{value:null},resolution:{value:new ae},radius:{value:4}},vertexShader:ty,fragmentShader:ny}),f=d.clone();f.defines.HORIZONTAL_PASS=1;let m=new Bt;m.setAttribute("position",new an(new Float32Array([-1,-1,.5,3,-1,.5,-1,3,.5]),3));let b=new ke(m,d),g=this;this.enabled=!1,this.autoUpdate=!0,this.needsUpdate=!1,this.type=Qa;let p=this.type;this.render=function(E,A,y){if(g.enabled===!1||g.autoUpdate===!1&&g.needsUpdate===!1||E.length===0)return;this.type===Fc&&(Ve("WebGLShadowMap: PCFSoftShadowMap has been removed. Using PCFShadowMap instead."),this.type=Qa);let w=i.getRenderTarget(),R=i.getActiveCubeFace(),I=i.getActiveMipmapLevel(),L=i.state;L.setBlending(Oi),L.buffers.depth.getReversed()===!0?L.buffers.color.setClear(0,0,0,0):L.buffers.color.setClear(1,1,1,1),L.buffers.depth.setTest(!0),L.setScissorTest(!1);let F=p!==this.type;F&&A.traverse(function(D){D.material&&(Array.isArray(D.material)?D.material.forEach(k=>k.needsUpdate=!0):D.material.needsUpdate=!0)});for(let D=0,k=E.length;D<k;D++){let W=E[D],X=W.shadow;if(X===void 0){Ve("WebGLShadowMap:",W,"has no shadow.");continue}if(X.autoUpdate===!1&&X.needsUpdate===!1)continue;s.copy(X.mapSize);let re=X.getFrameExtents();s.multiply(re),r.copy(X.mapSize),(s.x>h||s.y>h)&&(s.x>h&&(r.x=Math.floor(h/re.x),s.x=r.x*re.x,X.mapSize.x=r.x),s.y>h&&(r.y=Math.floor(h/re.y),s.y=r.y*re.y,X.mapSize.y=r.y));let j=i.state.buffers.depth.getReversed();if(X.camera._reversedDepth=j,X.map===null||F===!0){if(X.map!==null&&(X.map.depthTexture!==null&&(X.map.depthTexture.dispose(),X.map.depthTexture=null),X.map.dispose()),this.type===Xr){if(W.isPointLight){Ve("WebGLShadowMap: VSM shadow maps are not supported for PointLights. Use PCF or BasicShadowMap instead.");continue}X.map=new wn(s.x,s.y,{format:Ps,type:Si,minFilter:Xt,magFilter:Xt,generateMipmaps:!1}),X.map.texture.name=W.name+".shadowMap",X.map.depthTexture=new _s(s.x,s.y,Xn),X.map.depthTexture.name=W.name+".shadowMapDepth",X.map.depthTexture.format=Ii,X.map.depthTexture.compareFunction=null,X.map.depthTexture.minFilter=Wt,X.map.depthTexture.magFilter=Wt}else W.isPointLight?(X.map=new Al(s.x),X.map.depthTexture=new bc(s.x,Mi)):(X.map=new wn(s.x,s.y),X.map.depthTexture=new _s(s.x,s.y,Mi)),X.map.depthTexture.name=W.name+".shadowMap",X.map.depthTexture.format=Ii,this.type===Qa?(X.map.depthTexture.compareFunction=j?El:Sl,X.map.depthTexture.minFilter=Xt,X.map.depthTexture.magFilter=Xt):(X.map.depthTexture.compareFunction=null,X.map.depthTexture.minFilter=Wt,X.map.depthTexture.magFilter=Wt);X.camera.updateProjectionMatrix()}X.map.isWebGLCubeRenderTarget!==!0&&(X.map.width!==s.x||X.map.height!==s.y)&&X.map.setSize(s.x,s.y);let ee=X.map.isWebGLCubeRenderTarget?6:X.getViewportCount();W.isPointLight!==!0&&X.updateMatrices(W,y);for(let te=0;te<ee;te++){let Ne=X.getCamera(te);if(W.isPointLight){let Se=X.camera,ct=X.matrix,$e=W.distance||Se.far;$e!==Se.far&&(Se.far=$e,Se.updateProjectionMatrix()),fo.setFromMatrixPosition(W.matrixWorld),Se.position.copy(fo),Eu.copy(Se.position),Eu.add(iy[te]),Se.up.copy(sy[te]),Se.lookAt(Eu),Se.updateMatrixWorld(),ct.makeTranslation(-fo.x,-fo.y,-fo.z),Xp.multiplyMatrices(Se.projectionMatrix,Se.matrixWorldInverse),X._frustum.setFromProjectionMatrix(Xp,Se.coordinateSystem,Se.reversedDepth)}if(X.map.isWebGLCubeRenderTarget)i.setRenderTarget(X.map,te),i.clear();else{te===0&&(i.setRenderTarget(X.map),i.clear());let Se=X.getViewport(te);a.set(r.x*Se.x,r.y*Se.y,r.x*Se.z,r.y*Se.w),L.viewport(a)}n=X.getFrustum(te),x(A,y,Ne,W,this.type)}X.isPointLightShadow!==!0&&this.type===Xr&&_(X,y),X.needsUpdate=!1}p=this.type,g.needsUpdate=!1,i.setRenderTarget(w,R,I)};function _(E,A){let y=e.update(b);d.defines.VSM_SAMPLES!==E.blurSamples&&(d.defines.VSM_SAMPLES=E.blurSamples,f.defines.VSM_SAMPLES=E.blurSamples,d.needsUpdate=!0,f.needsUpdate=!0),E.mapPass===null?E.mapPass=new wn(s.x,s.y,{format:Ps,type:Si}):(E.mapPass.width!==E.map.width||E.mapPass.height!==E.map.height)&&E.mapPass.setSize(E.map.width,E.map.height),d.uniforms.shadow_pass.value=E.map.depthTexture,d.uniforms.resolution.value.set(E.map.width,E.map.height),d.uniforms.radius.value=E.radius,i.setRenderTarget(E.mapPass),i.clear(),i.renderBufferDirect(A,null,y,d,b,null),f.uniforms.shadow_pass.value=E.mapPass.texture,f.uniforms.resolution.value.set(E.map.width,E.map.height),f.uniforms.radius.value=E.radius,i.setRenderTarget(E.map),i.clear(),i.renderBufferDirect(A,null,y,f,b,null)}function S(E,A,y,w){let R=null,I=y.isPointLight===!0?E.customDistanceMaterial:E.customDepthMaterial;if(I!==void 0)R=I;else if(R=y.isPointLight===!0?c:o,i.localClippingEnabled&&A.clipShadows===!0&&Array.isArray(A.clippingPlanes)&&A.clippingPlanes.length!==0||A.displacementMap&&A.displacementScale!==0||A.alphaMap&&A.alphaTest>0||A.map&&A.alphaTest>0||A.alphaToCoverage===!0){let L=R.uuid,F=A.uuid,D=l[L];D===void 0&&(D={},l[L]=D);let k=D[F];k===void 0&&(k=R.clone(),D[F]=k,A.addEventListener("dispose",M)),R=k}if(R.visible=A.visible,R.wireframe=A.wireframe,w===Xr?R.side=A.shadowSide!==null?A.shadowSide:A.side:R.side=A.shadowSide!==null?A.shadowSide:u[A.side],R.alphaMap=A.alphaMap,R.alphaTest=A.alphaToCoverage===!0?.5:A.alphaTest,R.map=A.map,R.clipShadows=A.clipShadows,R.clippingPlanes=A.clippingPlanes,R.clipIntersection=A.clipIntersection,R.displacementMap=A.displacementMap,R.displacementScale=A.displacementScale,R.displacementBias=A.displacementBias,R.wireframeLinewidth=A.wireframeLinewidth,R.linewidth=A.linewidth,y.isPointLight===!0&&R.isMeshDistanceMaterial===!0){let L=i.properties.get(R);L.light=y}return R}function x(E,A,y,w,R){if(E.visible===!1)return;if(E.layers.test(A.layers)&&(E.isMesh||E.isLine||E.isPoints)&&(E.castShadow||E.receiveShadow&&R===Xr)&&(!E.frustumCulled||E.intersectsFrustum(n))){E.modelViewMatrix.multiplyMatrices(y.matrixWorldInverse,E.matrixWorld);let F=e.update(E),D=E.material;if(Array.isArray(D)){let k=F.groups;for(let W=0,X=k.length;W<X;W++){let re=k[W],j=D[re.materialIndex];if(j&&j.visible){let ee=S(E,j,w,R);E.onBeforeShadow(i,E,A,y,F,ee,re),i.renderBufferDirect(y,null,F,ee,E,re),E.onAfterShadow(i,E,A,y,F,ee,re)}}}else if(D.visible){let k=S(E,D,w,R);E.onBeforeShadow(i,E,A,y,F,k,null),i.renderBufferDirect(y,null,F,k,E,null),E.onAfterShadow(i,E,A,y,F,k,null)}}let L=E.children;for(let F=0,D=L.length;F<D;F++)x(L[F],A,y,w,R)}function M(E){E.target.removeEventListener("dispose",M);for(let y in l){let w=l[y],R=E.target.uuid;R in w&&(w[R].dispose(),delete w[R])}}}function ay(i,e){function t(){let U=!1,fe=new Pt,B=null,ue=new Pt(0,0,0,0);return{setMask:function(be){B!==be&&!U&&(i.colorMask(be,be,be,be),B=be)},setLocked:function(be){U=be},setClear:function(be,se,Ie,Pe,kt){kt===!0&&(be*=Pe,se*=Pe,Ie*=Pe),fe.set(be,se,Ie,Pe),ue.equals(fe)===!1&&(i.clearColor(be,se,Ie,Pe),ue.copy(fe))},reset:function(){U=!1,B=null,ue.set(-1,0,0,0)}}}function n(){let U=!1,fe=!1,B=null,ue=null,be=null;return{setReversed:function(se){if(fe!==se){let Ie=e.get("EXT_clip_control");se?Ie.clipControlEXT(Ie.LOWER_LEFT_EXT,Ie.ZERO_TO_ONE_EXT):Ie.clipControlEXT(Ie.LOWER_LEFT_EXT,Ie.NEGATIVE_ONE_TO_ONE_EXT),fe=se;let Pe=be;be=null,this.setClear(Pe)}},getReversed:function(){return fe},setTest:function(se){se?ie(i.DEPTH_TEST):ve(i.DEPTH_TEST)},setMask:function(se){B!==se&&!U&&(i.depthMask(se),B=se)},setFunc:function(se){if(fe&&(se=bp[se]),ue!==se){switch(se){case rc:i.depthFunc(i.NEVER);break;case ac:i.depthFunc(i.ALWAYS);break;case oc:i.depthFunc(i.LESS);break;case Ar:i.depthFunc(i.LEQUAL);break;case cc:i.depthFunc(i.EQUAL);break;case lc:i.depthFunc(i.GEQUAL);break;case hc:i.depthFunc(i.GREATER);break;case uc:i.depthFunc(i.NOTEQUAL);break;default:i.depthFunc(i.LEQUAL)}ue=se}},setLocked:function(se){U=se},setClear:function(se){be!==se&&(be=se,fe&&(se=1-se),i.clearDepth(se))},reset:function(){U=!1,B=null,ue=null,be=null,fe=!1}}}function s(){let U=!1,fe=null,B=null,ue=null,be=null,se=null,Ie=null,Pe=null,kt=null;return{setTest:function(Et){U||(Et?ie(i.STENCIL_TEST):ve(i.STENCIL_TEST))},setMask:function(Et){fe!==Et&&!U&&(i.stencilMask(Et),fe=Et)},setFunc:function(Et,fi,Ti){(B!==Et||ue!==fi||be!==Ti)&&(i.stencilFunc(Et,fi,Ti),B=Et,ue=fi,be=Ti)},setOp:function(Et,fi,Ti){(se!==Et||Ie!==fi||Pe!==Ti)&&(i.stencilOp(Et,fi,Ti),se=Et,Ie=fi,Pe=Ti)},setLocked:function(Et){U=Et},setClear:function(Et){kt!==Et&&(i.clearStencil(Et),kt=Et)},reset:function(){U=!1,fe=null,B=null,ue=null,be=null,se=null,Ie=null,Pe=null,kt=null}}}let r=new t,a=new n,o=new s,c=new WeakMap,l=new WeakMap,h={},u={},d={},f=new WeakMap,m=[],b=null,g=!1,p=null,_=null,S=null,x=null,M=null,E=null,A=null,y=new Be(0,0,0),w=0,R=!1,I=null,L=null,F=null,D=null,k=null,W=i.getParameter(i.MAX_COMBINED_TEXTURE_IMAGE_UNITS),X=!1,re=0,j=i.getParameter(i.VERSION);j.indexOf("WebGL")!==-1?(re=parseFloat(/^WebGL (\d)/.exec(j)[1]),X=re>=1):j.indexOf("OpenGL ES")!==-1&&(re=parseFloat(/^OpenGL ES (\d)/.exec(j)[1]),X=re>=2);let ee=null,te={},Ne=i.getParameter(i.SCISSOR_BOX),Se=i.getParameter(i.VIEWPORT),ct=new Pt().fromArray(Ne),$e=new Pt().fromArray(Se);function ht(U,fe,B,ue){let be=new Uint8Array(4),se=i.createTexture();i.bindTexture(U,se),i.texParameteri(U,i.TEXTURE_MIN_FILTER,i.NEAREST),i.texParameteri(U,i.TEXTURE_MAG_FILTER,i.NEAREST);for(let Ie=0;Ie<B;Ie++)U===i.TEXTURE_3D||U===i.TEXTURE_2D_ARRAY?i.texImage3D(fe,0,i.RGBA,1,1,ue,0,i.RGBA,i.UNSIGNED_BYTE,be):i.texImage2D(fe+Ie,0,i.RGBA,1,1,0,i.RGBA,i.UNSIGNED_BYTE,be);return se}let J={};J[i.TEXTURE_2D]=ht(i.TEXTURE_2D,i.TEXTURE_2D,1),J[i.TEXTURE_CUBE_MAP]=ht(i.TEXTURE_CUBE_MAP,i.TEXTURE_CUBE_MAP_POSITIVE_X,6),J[i.TEXTURE_2D_ARRAY]=ht(i.TEXTURE_2D_ARRAY,i.TEXTURE_2D_ARRAY,1,1),J[i.TEXTURE_3D]=ht(i.TEXTURE_3D,i.TEXTURE_3D,1,1),r.setClear(0,0,0,1),a.setClear(1),o.setClear(0),ie(i.DEPTH_TEST),a.setFunc(Ar),de(!1),xe(qh),ie(i.CULL_FACE),ce(Oi);function ie(U){h[U]!==!0&&(i.enable(U),h[U]=!0)}function ve(U){h[U]!==!1&&(i.disable(U),h[U]=!1)}function Ke(U,fe){return d[U]!==fe?(i.bindFramebuffer(U,fe),d[U]=fe,U===i.DRAW_FRAMEBUFFER&&(d[i.FRAMEBUFFER]=fe),U===i.FRAMEBUFFER&&(d[i.DRAW_FRAMEBUFFER]=fe),!0):!1}function Ae(U,fe){let B=m,ue=!1;if(U){B=f.get(fe),B===void 0&&(B=[],f.set(fe,B));let be=U.textures;if(B.length!==be.length||B[0]!==i.COLOR_ATTACHMENT0){for(let se=0,Ie=be.length;se<Ie;se++)B[se]=i.COLOR_ATTACHMENT0+se;B.length=be.length,ue=!0}}else B[0]!==i.BACK&&(B[0]=i.BACK,ue=!0);ue&&i.drawBuffers(B)}function ze(U){return b!==U?(i.useProgram(U),b=U,!0):!1}let bt={[er]:i.FUNC_ADD,[Bf]:i.FUNC_SUBTRACT,[kf]:i.FUNC_REVERSE_SUBTRACT};bt[zf]=i.MIN,bt[Hf]=i.MAX;let Q={[Gf]:i.ZERO,[Vf]:i.ONE,[Wf]:i.SRC_COLOR,[Yh]:i.SRC_ALPHA,[Jf]:i.SRC_ALPHA_SATURATE,[Kf]:i.DST_COLOR,[Xf]:i.DST_ALPHA,[qf]:i.ONE_MINUS_SRC_COLOR,[Jh]:i.ONE_MINUS_SRC_ALPHA,[Yf]:i.ONE_MINUS_DST_COLOR,[jf]:i.ONE_MINUS_DST_ALPHA,[Zf]:i.CONSTANT_COLOR,[$f]:i.ONE_MINUS_CONSTANT_COLOR,[Qf]:i.CONSTANT_ALPHA,[ep]:i.ONE_MINUS_CONSTANT_ALPHA};function ce(U,fe,B,ue,be,se,Ie,Pe,kt,Et){if(U===Oi){g===!0&&(ve(i.BLEND),g=!1);return}if(g===!1&&(ie(i.BLEND),g=!0),U!==Of){if(U!==p||Et!==R){if((_!==er||M!==er)&&(i.blendEquation(i.FUNC_ADD),_=er,M=er),Et)switch(U){case jr:i.blendFuncSeparate(i.ONE,i.ONE_MINUS_SRC_ALPHA,i.ONE,i.ONE_MINUS_SRC_ALPHA);break;case Xh:i.blendFunc(i.ONE,i.ONE);break;case jh:i.blendFuncSeparate(i.ZERO,i.ONE_MINUS_SRC_COLOR,i.ZERO,i.ONE);break;case Kh:i.blendFuncSeparate(i.DST_COLOR,i.ONE_MINUS_SRC_ALPHA,i.ZERO,i.ONE);break;default:Ye("WebGLState: Invalid blending: ",U);break}else switch(U){case jr:i.blendFuncSeparate(i.SRC_ALPHA,i.ONE_MINUS_SRC_ALPHA,i.ONE,i.ONE_MINUS_SRC_ALPHA);break;case Xh:i.blendFuncSeparate(i.SRC_ALPHA,i.ONE,i.ONE,i.ONE);break;case jh:Ye("WebGLState: SubtractiveBlending requires material.premultipliedAlpha = true");break;case Kh:Ye("WebGLState: MultiplyBlending requires material.premultipliedAlpha = true");break;default:Ye("WebGLState: Invalid blending: ",U);break}S=null,x=null,E=null,A=null,y.set(0,0,0),w=0,p=U,R=Et}return}be=be||fe,se=se||B,Ie=Ie||ue,(fe!==_||be!==M)&&(i.blendEquationSeparate(bt[fe],bt[be]),_=fe,M=be),(B!==S||ue!==x||se!==E||Ie!==A)&&(i.blendFuncSeparate(Q[B],Q[ue],Q[se],Q[Ie]),S=B,x=ue,E=se,A=Ie),(Pe.equals(y)===!1||kt!==w)&&(i.blendColor(Pe.r,Pe.g,Pe.b,kt),y.copy(Pe),w=kt),p=U,R=!1}function le(U,fe){U.side===oi?ve(i.CULL_FACE):ie(i.CULL_FACE);let B=U.side===yn;fe&&(B=!B),de(B),U.blending===jr&&U.transparent===!1?ce(Oi):ce(U.blending,U.blendEquation,U.blendSrc,U.blendDst,U.blendEquationAlpha,U.blendSrcAlpha,U.blendDstAlpha,U.blendColor,U.blendAlpha,U.premultipliedAlpha),a.setFunc(U.depthFunc),a.setTest(U.depthTest),a.setMask(U.depthWrite),r.setMask(U.colorWrite);let ue=U.stencilWrite;o.setTest(ue),ue&&(o.setMask(U.stencilWriteMask),o.setFunc(U.stencilFunc,U.stencilRef,U.stencilFuncMask),o.setOp(U.stencilFail,U.stencilZFail,U.stencilZPass)),Fe(U.polygonOffset,U.polygonOffsetFactor,U.polygonOffsetUnits),U.alphaToCoverage===!0?ie(i.SAMPLE_ALPHA_TO_COVERAGE):ve(i.SAMPLE_ALPHA_TO_COVERAGE)}function de(U){I!==U&&(U?i.frontFace(i.CW):i.frontFace(i.CCW),I=U)}function xe(U){U!==Ff?(ie(i.CULL_FACE),U!==L&&(U===qh?i.cullFace(i.BACK):U===Uf?i.cullFace(i.FRONT):i.cullFace(i.FRONT_AND_BACK))):ve(i.CULL_FACE),L=U}function Xe(U){U!==F&&(X&&i.lineWidth(U),F=U)}function Fe(U,fe,B){U?(ie(i.POLYGON_OFFSET_FILL),(D!==fe||k!==B)&&(D=fe,k=B,a.getReversed()&&(fe=-fe),i.polygonOffset(fe,B))):ve(i.POLYGON_OFFSET_FILL)}function qe(U){U?ie(i.SCISSOR_TEST):ve(i.SCISSOR_TEST)}function Je(U){U===void 0&&(U=i.TEXTURE0+W-1),ee!==U&&(i.activeTexture(U),ee=U)}function N(U,fe,B){B===void 0&&(ee===null?B=i.TEXTURE0+W-1:B=ee);let ue=te[B];ue===void 0&&(ue={type:void 0,texture:void 0},te[B]=ue),(ue.type!==U||ue.texture!==fe)&&(ee!==B&&(i.activeTexture(B),ee=B),i.bindTexture(U,fe||J[U]),ue.type=U,ue.texture=fe)}function xt(){let U=te[ee];U!==void 0&&U.type!==void 0&&(i.bindTexture(U.type,null),U.type=void 0,U.texture=void 0)}function st(){try{i.compressedTexImage2D(...arguments)}catch(U){Ye("WebGLState:",U)}}function C(){try{i.compressedTexImage3D(...arguments)}catch(U){Ye("WebGLState:",U)}}function v(){try{i.texSubImage2D(...arguments)}catch(U){Ye("WebGLState:",U)}}function z(){try{i.texSubImage3D(...arguments)}catch(U){Ye("WebGLState:",U)}}function V(){try{i.compressedTexSubImage2D(...arguments)}catch(U){Ye("WebGLState:",U)}}function Y(){try{i.compressedTexSubImage3D(...arguments)}catch(U){Ye("WebGLState:",U)}}function pe(){try{i.texStorage2D(...arguments)}catch(U){Ye("WebGLState:",U)}}function me(){try{i.texStorage3D(...arguments)}catch(U){Ye("WebGLState:",U)}}function Z(){try{i.texImage2D(...arguments)}catch(U){Ye("WebGLState:",U)}}function ne(){try{i.texImage3D(...arguments)}catch(U){Ye("WebGLState:",U)}}function ye(U){return u[U]!==void 0?u[U]:i.getParameter(U)}function He(U,fe){u[U]!==fe&&(i.pixelStorei(U,fe),u[U]=fe)}function Me(U){ct.equals(U)===!1&&(i.scissor(U.x,U.y,U.z,U.w),ct.copy(U))}function q(U){$e.equals(U)===!1&&(i.viewport(U.x,U.y,U.z,U.w),$e.copy(U))}function oe(U,fe){let B=l.get(fe);B===void 0&&(B=new WeakMap,l.set(fe,B));let ue=B.get(U);ue===void 0&&(ue=i.getUniformBlockIndex(fe,U.name),B.set(U,ue))}function ge(U,fe){let ue=l.get(fe).get(U);c.get(fe)!==ue&&(i.uniformBlockBinding(fe,ue,U.__bindingPointIndex),c.set(fe,ue))}function je(){i.disable(i.BLEND),i.disable(i.CULL_FACE),i.disable(i.DEPTH_TEST),i.disable(i.POLYGON_OFFSET_FILL),i.disable(i.SCISSOR_TEST),i.disable(i.STENCIL_TEST),i.disable(i.SAMPLE_ALPHA_TO_COVERAGE),i.blendEquation(i.FUNC_ADD),i.blendFunc(i.ONE,i.ZERO),i.blendFuncSeparate(i.ONE,i.ZERO,i.ONE,i.ZERO),i.blendColor(0,0,0,0),i.colorMask(!0,!0,!0,!0),i.clearColor(0,0,0,0),i.depthMask(!0),i.depthFunc(i.LESS),a.setReversed(!1),i.clearDepth(1),i.stencilMask(4294967295),i.stencilFunc(i.ALWAYS,0,4294967295),i.stencilOp(i.KEEP,i.KEEP,i.KEEP),i.clearStencil(0),i.cullFace(i.BACK),i.frontFace(i.CCW),i.polygonOffset(0,0),i.activeTexture(i.TEXTURE0),i.bindFramebuffer(i.FRAMEBUFFER,null),i.bindFramebuffer(i.DRAW_FRAMEBUFFER,null),i.bindFramebuffer(i.READ_FRAMEBUFFER,null),i.useProgram(null),i.lineWidth(1),i.scissor(0,0,i.canvas.width,i.canvas.height),i.viewport(0,0,i.canvas.width,i.canvas.height),i.pixelStorei(i.PACK_ALIGNMENT,4),i.pixelStorei(i.UNPACK_ALIGNMENT,4),i.pixelStorei(i.UNPACK_FLIP_Y_WEBGL,!1),i.pixelStorei(i.UNPACK_PREMULTIPLY_ALPHA_WEBGL,!1),i.pixelStorei(i.UNPACK_COLORSPACE_CONVERSION_WEBGL,i.BROWSER_DEFAULT_WEBGL),i.pixelStorei(i.PACK_ROW_LENGTH,0),i.pixelStorei(i.PACK_SKIP_PIXELS,0),i.pixelStorei(i.PACK_SKIP_ROWS,0),i.pixelStorei(i.UNPACK_ROW_LENGTH,0),i.pixelStorei(i.UNPACK_IMAGE_HEIGHT,0),i.pixelStorei(i.UNPACK_SKIP_PIXELS,0),i.pixelStorei(i.UNPACK_SKIP_ROWS,0),i.pixelStorei(i.UNPACK_SKIP_IMAGES,0),h={},u={},ee=null,te={},d={},f=new WeakMap,m=[],b=null,g=!1,p=null,_=null,S=null,x=null,M=null,E=null,A=null,y=new Be(0,0,0),w=0,R=!1,I=null,L=null,F=null,D=null,k=null,ct.set(0,0,i.canvas.width,i.canvas.height),$e.set(0,0,i.canvas.width,i.canvas.height),r.reset(),a.reset(),o.reset()}return{buffers:{color:r,depth:a,stencil:o},enable:ie,disable:ve,bindFramebuffer:Ke,drawBuffers:Ae,useProgram:ze,setBlending:ce,setMaterial:le,setFlipSided:de,setCullFace:xe,setLineWidth:Xe,setPolygonOffset:Fe,setScissorTest:qe,activeTexture:Je,bindTexture:N,unbindTexture:xt,compressedTexImage2D:st,compressedTexImage3D:C,texImage2D:Z,texImage3D:ne,pixelStorei:He,getParameter:ye,updateUBOMapping:oe,uniformBlockBinding:ge,texStorage2D:pe,texStorage3D:me,texSubImage2D:v,texSubImage3D:z,compressedTexSubImage2D:V,compressedTexSubImage3D:Y,scissor:Me,viewport:q,reset:je}}function oy(i,e,t,n,s,r,a){let o=e.has("WEBGL_multisampled_render_to_texture")?e.get("WEBGL_multisampled_render_to_texture"):null,c=typeof navigator>"u"?!1:/OculusBrowser/g.test(navigator.userAgent),l=new ae,h=new WeakMap,u=new Set,d,f=new WeakMap,m=!1;try{m=typeof OffscreenCanvas<"u"&&new OffscreenCanvas(1,1).getContext("2d")!==null}catch{}function b(C,v){return m?new OffscreenCanvas(C,v):Pr("canvas")}function g(C,v,z){let V=1,Y=st(C);if((Y.width>z||Y.height>z)&&(V=z/Math.max(Y.width,Y.height)),V<1)if(typeof HTMLImageElement<"u"&&C instanceof HTMLImageElement||typeof HTMLCanvasElement<"u"&&C instanceof HTMLCanvasElement||typeof ImageBitmap<"u"&&C instanceof ImageBitmap||typeof VideoFrame<"u"&&C instanceof VideoFrame){let pe=Math.floor(V*Y.width),me=Math.floor(V*Y.height);d===void 0&&(d=b(pe,me));let Z=v?b(pe,me):d;return Z.width=pe,Z.height=me,Z.getContext("2d").drawImage(C,0,0,pe,me),Ve("WebGLRenderer: Texture has been resized from ("+Y.width+"x"+Y.height+") to ("+pe+"x"+me+")."),Z}else return"data"in C&&Ve("WebGLRenderer: Image in DataTexture is too big ("+Y.width+"x"+Y.height+")."),C;return C}function p(C){return C.generateMipmaps}function _(C){i.generateMipmap(C)}function S(C){return C.isWebGLCubeRenderTarget?i.TEXTURE_CUBE_MAP:C.isWebGL3DRenderTarget?i.TEXTURE_3D:C.isWebGLArrayRenderTarget||C.isCompressedArrayTexture?i.TEXTURE_2D_ARRAY:i.TEXTURE_2D}function x(C,v,z,V,Y,pe=!1){if(C!==null){if(i[C]!==void 0)return i[C];Ve("WebGLRenderer: Attempt to use non-existing WebGL internal format '"+C+"'")}let me;V&&(me=e.get("EXT_texture_norm16"),me||Ve("WebGLRenderer: Unable to use normalized textures without EXT_texture_norm16 extension"));let Z=v;if(v===i.RED&&(z===i.FLOAT&&(Z=i.R32F),z===i.HALF_FLOAT&&(Z=i.R16F),z===i.UNSIGNED_BYTE&&(Z=i.R8),z===i.UNSIGNED_SHORT&&me&&(Z=me.R16_EXT),z===i.SHORT&&me&&(Z=me.R16_SNORM_EXT)),v===i.RED_INTEGER&&(z===i.UNSIGNED_BYTE&&(Z=i.R8UI),z===i.UNSIGNED_SHORT&&(Z=i.R16UI),z===i.UNSIGNED_INT&&(Z=i.R32UI),z===i.BYTE&&(Z=i.R8I),z===i.SHORT&&(Z=i.R16I),z===i.INT&&(Z=i.R32I)),v===i.RG&&(z===i.FLOAT&&(Z=i.RG32F),z===i.HALF_FLOAT&&(Z=i.RG16F),z===i.UNSIGNED_BYTE&&(Z=i.RG8),z===i.UNSIGNED_SHORT&&me&&(Z=me.RG16_EXT),z===i.SHORT&&me&&(Z=me.RG16_SNORM_EXT)),v===i.RG_INTEGER&&(z===i.UNSIGNED_BYTE&&(Z=i.RG8UI),z===i.UNSIGNED_SHORT&&(Z=i.RG16UI),z===i.UNSIGNED_INT&&(Z=i.RG32UI),z===i.BYTE&&(Z=i.RG8I),z===i.SHORT&&(Z=i.RG16I),z===i.INT&&(Z=i.RG32I)),v===i.RGB_INTEGER&&(z===i.UNSIGNED_BYTE&&(Z=i.RGB8UI),z===i.UNSIGNED_SHORT&&(Z=i.RGB16UI),z===i.UNSIGNED_INT&&(Z=i.RGB32UI),z===i.BYTE&&(Z=i.RGB8I),z===i.SHORT&&(Z=i.RGB16I),z===i.INT&&(Z=i.RGB32I)),v===i.RGBA_INTEGER&&(z===i.UNSIGNED_BYTE&&(Z=i.RGBA8UI),z===i.UNSIGNED_SHORT&&(Z=i.RGBA16UI),z===i.UNSIGNED_INT&&(Z=i.RGBA32UI),z===i.BYTE&&(Z=i.RGBA8I),z===i.SHORT&&(Z=i.RGBA16I),z===i.INT&&(Z=i.RGBA32I)),v===i.RGB&&(z===i.UNSIGNED_SHORT&&me&&(Z=me.RGB16_EXT),z===i.SHORT&&me&&(Z=me.RGB16_SNORM_EXT),z===i.UNSIGNED_INT_5_9_9_9_REV&&(Z=i.RGB9_E5),z===i.UNSIGNED_INT_10F_11F_11F_REV&&(Z=i.R11F_G11F_B10F)),v===i.RGBA){let ne=pe?Ea:lt.getTransfer(Y);z===i.FLOAT&&(Z=i.RGBA32F),z===i.HALF_FLOAT&&(Z=i.RGBA16F),z===i.UNSIGNED_BYTE&&(Z=ne===wt?i.SRGB8_ALPHA8:i.RGBA8),z===i.UNSIGNED_SHORT&&me&&(Z=me.RGBA16_EXT),z===i.SHORT&&me&&(Z=me.RGBA16_SNORM_EXT),z===i.UNSIGNED_SHORT_4_4_4_4&&(Z=i.RGBA4),z===i.UNSIGNED_SHORT_5_5_5_1&&(Z=i.RGB5_A1)}return(Z===i.R16F||Z===i.R32F||Z===i.RG16F||Z===i.RG32F||Z===i.RGBA16F||Z===i.RGBA32F)&&e.get("EXT_color_buffer_float"),Z}function M(C,v){let z;return C?v===null||v===Mi||v===Jr?z=i.DEPTH24_STENCIL8:v===Xn?z=i.DEPTH32F_STENCIL8:v===Yr&&(z=i.DEPTH24_STENCIL8,Ve("DepthTexture: 16 bit depth attachment is not supported with stencil. Using 24-bit attachment.")):v===null||v===Mi||v===Jr?z=i.DEPTH_COMPONENT24:v===Xn?z=i.DEPTH_COMPONENT32F:v===Yr&&(z=i.DEPTH_COMPONENT16),z}function E(C,v){return p(C)===!0||C.isFramebufferTexture&&C.minFilter!==Wt&&C.minFilter!==Xt?Math.log2(Math.max(v.width,v.height))+1:C.mipmaps!==void 0&&C.mipmaps.length>0?C.mipmaps.length:C.isCompressedTexture&&Array.isArray(C.image)?v.mipmaps.length:1}function A(C){let v=C.target;v.removeEventListener("dispose",A),w(v),v.isVideoTexture&&h.delete(v),v.isHTMLTexture&&u.delete(v)}function y(C){let v=C.target;v.removeEventListener("dispose",y),I(v)}function w(C){let v=n.get(C);if(v.__webglInit===void 0)return;let z=C.source,V=f.get(z);if(V){let Y=V[v.__cacheKey];Y.usedTimes--,Y.usedTimes===0&&R(C),Object.keys(V).length===0&&f.delete(z)}n.remove(C)}function R(C){let v=n.get(C);i.deleteTexture(v.__webglTexture);let z=C.source,V=f.get(z);delete V[v.__cacheKey],a.memory.textures--}function I(C){let v=n.get(C);if(C.depthTexture&&(C.depthTexture.dispose(),n.remove(C.depthTexture)),C.isWebGLCubeRenderTarget)for(let V=0;V<6;V++){if(Array.isArray(v.__webglFramebuffer[V]))for(let Y=0;Y<v.__webglFramebuffer[V].length;Y++)i.deleteFramebuffer(v.__webglFramebuffer[V][Y]);else i.deleteFramebuffer(v.__webglFramebuffer[V]);v.__webglDepthbuffer&&i.deleteRenderbuffer(v.__webglDepthbuffer[V])}else{if(Array.isArray(v.__webglFramebuffer))for(let V=0;V<v.__webglFramebuffer.length;V++)i.deleteFramebuffer(v.__webglFramebuffer[V]);else i.deleteFramebuffer(v.__webglFramebuffer);if(v.__webglDepthbuffer&&i.deleteRenderbuffer(v.__webglDepthbuffer),v.__webglMultisampledFramebuffer&&i.deleteFramebuffer(v.__webglMultisampledFramebuffer),v.__webglColorRenderbuffer)for(let V=0;V<v.__webglColorRenderbuffer.length;V++)v.__webglColorRenderbuffer[V]&&i.deleteRenderbuffer(v.__webglColorRenderbuffer[V]);v.__webglDepthRenderbuffer&&i.deleteRenderbuffer(v.__webglDepthRenderbuffer)}let z=C.textures;for(let V=0,Y=z.length;V<Y;V++){let pe=n.get(z[V]);pe.__webglTexture&&(i.deleteTexture(pe.__webglTexture),a.memory.textures--),n.remove(z[V])}n.remove(C)}let L=0;function F(){L=0}function D(){return L}function k(C){L=C}function W(){let C=L;return C>=s.maxTextures&&Ve("WebGLTextures: Trying to use "+(C+1)+" texture units while this GPU supports only "+s.maxTextures),L+=1,C}function X(C){let v=[];return v.push(C.wrapS),v.push(C.wrapT),v.push(C.wrapR||0),v.push(C.magFilter),v.push(C.minFilter),v.push(C.anisotropy),v.push(C.internalFormat),v.push(C.format),v.push(C.type),v.push(C.generateMipmaps),v.push(C.premultiplyAlpha),v.push(C.flipY),v.push(C.unpackAlignment),v.push(C.colorSpace),v.join()}function re(C,v){let z=n.get(C);if(C.isVideoTexture&&N(C),C.isRenderTargetTexture===!1&&C.isExternalTexture!==!0&&C.version>0&&z.__version!==C.version){let V=C.image;if(V===null)Ve("WebGLRenderer: Texture marked for update but no image data found.");else if(V.complete===!1)Ve("WebGLRenderer: Texture marked for update but image is incomplete");else{ve(z,C,v);return}}else C.isExternalTexture&&(z.__webglTexture=C.sourceTexture?C.sourceTexture:null);t.bindTexture(i.TEXTURE_2D,z.__webglTexture,i.TEXTURE0+v)}function j(C,v){let z=n.get(C);if(C.isRenderTargetTexture===!1&&C.version>0&&z.__version!==C.version){ve(z,C,v);return}else C.isExternalTexture&&(z.__webglTexture=C.sourceTexture?C.sourceTexture:null);t.bindTexture(i.TEXTURE_2D_ARRAY,z.__webglTexture,i.TEXTURE0+v)}function ee(C,v){let z=n.get(C);if(C.isRenderTargetTexture===!1&&C.version>0&&z.__version!==C.version){ve(z,C,v);return}t.bindTexture(i.TEXTURE_3D,z.__webglTexture,i.TEXTURE0+v)}function te(C,v){let z=n.get(C);if(C.isCubeDepthTexture!==!0&&C.version>0&&z.__version!==C.version){Ke(z,C,v);return}t.bindTexture(i.TEXTURE_CUBE_MAP,z.__webglTexture,i.TEXTURE0+v)}let Ne={[Pi]:i.REPEAT,[ni]:i.CLAMP_TO_EDGE,[Rr]:i.MIRRORED_REPEAT},Se={[Wt]:i.NEAREST,[kc]:i.NEAREST_MIPMAP_NEAREST,[nr]:i.NEAREST_MIPMAP_LINEAR,[Xt]:i.LINEAR,[Kr]:i.LINEAR_MIPMAP_NEAREST,[qn]:i.LINEAR_MIPMAP_LINEAR},ct={[cp]:i.NEVER,[fp]:i.ALWAYS,[lp]:i.LESS,[Sl]:i.LEQUAL,[hp]:i.EQUAL,[El]:i.GEQUAL,[up]:i.GREATER,[dp]:i.NOTEQUAL};function $e(C,v){if(v.type===Xn&&e.has("OES_texture_float_linear")===!1&&(v.magFilter===Xt||v.magFilter===Kr||v.magFilter===nr||v.magFilter===qn||v.minFilter===Xt||v.minFilter===Kr||v.minFilter===nr||v.minFilter===qn)&&Ve("WebGLRenderer: Unable to use linear filtering with floating point textures. OES_texture_float_linear not supported on this device."),i.texParameteri(C,i.TEXTURE_WRAP_S,Ne[v.wrapS]),i.texParameteri(C,i.TEXTURE_WRAP_T,Ne[v.wrapT]),(C===i.TEXTURE_3D||C===i.TEXTURE_2D_ARRAY)&&i.texParameteri(C,i.TEXTURE_WRAP_R,Ne[v.wrapR]),i.texParameteri(C,i.TEXTURE_MAG_FILTER,Se[v.magFilter]),i.texParameteri(C,i.TEXTURE_MIN_FILTER,Se[v.minFilter]),v.compareFunction&&(i.texParameteri(C,i.TEXTURE_COMPARE_MODE,i.COMPARE_REF_TO_TEXTURE),i.texParameteri(C,i.TEXTURE_COMPARE_FUNC,ct[v.compareFunction])),e.has("EXT_texture_filter_anisotropic")===!0){if(v.magFilter===Wt||v.minFilter!==nr&&v.minFilter!==qn||v.type===Xn&&e.has("OES_texture_float_linear")===!1)return;if(v.anisotropy>1||n.get(v).__currentAnisotropy){let z=e.get("EXT_texture_filter_anisotropic");i.texParameterf(C,z.TEXTURE_MAX_ANISOTROPY_EXT,Math.min(v.anisotropy,s.getMaxAnisotropy())),n.get(v).__currentAnisotropy=v.anisotropy}}}function ht(C,v){let z=!1;C.__webglInit===void 0&&(C.__webglInit=!0,v.addEventListener("dispose",A));let V=v.source,Y=f.get(V);Y===void 0&&(Y={},f.set(V,Y));let pe=X(v);if(pe!==C.__cacheKey){Y[pe]===void 0&&(Y[pe]={texture:i.createTexture(),usedTimes:0},a.memory.textures++,z=!0),Y[pe].usedTimes++;let me=Y[C.__cacheKey];me!==void 0&&(Y[C.__cacheKey].usedTimes--,me.usedTimes===0&&R(v)),C.__cacheKey=pe,C.__webglTexture=Y[pe].texture}return z}function J(C,v,z){return Math.floor(Math.floor(C/z)/v)}function ie(C,v,z,V){let pe=C.updateRanges;if(pe.length===0)t.texSubImage2D(i.TEXTURE_2D,0,0,0,v.width,v.height,z,V,v.data);else{pe.sort((He,Me)=>He.start-Me.start);let me=0;for(let He=1;He<pe.length;He++){let Me=pe[me],q=pe[He],oe=Me.start+Me.count,ge=J(q.start,v.width,4),je=J(Me.start,v.width,4);q.start<=oe+1&&ge===je&&J(q.start+q.count-1,v.width,4)===ge?Me.count=Math.max(Me.count,q.start+q.count-Me.start):(++me,pe[me]=q)}pe.length=me+1;let Z=t.getParameter(i.UNPACK_ROW_LENGTH),ne=t.getParameter(i.UNPACK_SKIP_PIXELS),ye=t.getParameter(i.UNPACK_SKIP_ROWS);t.pixelStorei(i.UNPACK_ROW_LENGTH,v.width);for(let He=0,Me=pe.length;He<Me;He++){let q=pe[He],oe=Math.floor(q.start/4),ge=Math.ceil(q.count/4),je=oe%v.width,U=Math.floor(oe/v.width),fe=ge,B=1;t.pixelStorei(i.UNPACK_SKIP_PIXELS,je),t.pixelStorei(i.UNPACK_SKIP_ROWS,U),t.texSubImage2D(i.TEXTURE_2D,0,je,U,fe,B,z,V,v.data)}C.clearUpdateRanges(),t.pixelStorei(i.UNPACK_ROW_LENGTH,Z),t.pixelStorei(i.UNPACK_SKIP_PIXELS,ne),t.pixelStorei(i.UNPACK_SKIP_ROWS,ye)}}function ve(C,v,z){let V=i.TEXTURE_2D;(v.isDataArrayTexture||v.isCompressedArrayTexture)&&(V=i.TEXTURE_2D_ARRAY),v.isData3DTexture&&(V=i.TEXTURE_3D);let Y=ht(C,v),pe=v.source;t.bindTexture(V,C.__webglTexture,i.TEXTURE0+z);let me=n.get(pe);if(pe.version!==me.__version||Y===!0){if(t.activeTexture(i.TEXTURE0+z),(typeof ImageBitmap<"u"&&v.image instanceof ImageBitmap)===!1){let B=lt.getPrimaries(lt.workingColorSpace),ue=v.colorSpace===rs?null:lt.getPrimaries(v.colorSpace),be=v.colorSpace===rs||B===ue?i.NONE:i.BROWSER_DEFAULT_WEBGL;t.pixelStorei(i.UNPACK_FLIP_Y_WEBGL,v.flipY),t.pixelStorei(i.UNPACK_PREMULTIPLY_ALPHA_WEBGL,v.premultiplyAlpha),t.pixelStorei(i.UNPACK_COLORSPACE_CONVERSION_WEBGL,be)}t.pixelStorei(i.UNPACK_ALIGNMENT,v.unpackAlignment);let ne=g(v.image,!1,s.maxTextureSize);ne=xt(v,ne);let ye=r.convert(v.format,v.colorSpace),He=r.convert(v.type),Me=x(v.internalFormat,ye,He,v.normalized,v.colorSpace,v.isVideoTexture);$e(V,v);let q,oe=v.mipmaps,ge=v.isVideoTexture!==!0,je=me.__version===void 0||Y===!0,U=pe.dataReady,fe=E(v,ne);if(v.isDepthTexture)Me=M(v.format===Cs,v.type),je&&(ge?t.texStorage2D(i.TEXTURE_2D,1,Me,ne.width,ne.height):t.texImage2D(i.TEXTURE_2D,0,Me,ne.width,ne.height,0,ye,He,null));else if(v.isDataTexture)if(oe.length>0){ge&&je&&t.texStorage2D(i.TEXTURE_2D,fe,Me,oe[0].width,oe[0].height);for(let B=0,ue=oe.length;B<ue;B++)q=oe[B],ge?U&&t.texSubImage2D(i.TEXTURE_2D,B,0,0,q.width,q.height,ye,He,q.data):t.texImage2D(i.TEXTURE_2D,B,Me,q.width,q.height,0,ye,He,q.data);v.generateMipmaps=!1}else ge?(je&&t.texStorage2D(i.TEXTURE_2D,fe,Me,ne.width,ne.height),U&&ie(v,ne,ye,He)):t.texImage2D(i.TEXTURE_2D,0,Me,ne.width,ne.height,0,ye,He,ne.data);else if(v.isCompressedTexture)if(v.isCompressedArrayTexture){ge&&je&&t.texStorage3D(i.TEXTURE_2D_ARRAY,fe,Me,oe[0].width,oe[0].height,ne.depth);for(let B=0,ue=oe.length;B<ue;B++)if(q=oe[B],v.format!==jn)if(ye!==null)if(ge){if(U)if(v.layerUpdates.size>0){let be=xu(q.width,q.height,v.format,v.type);for(let se of v.layerUpdates){let Ie=q.data.subarray(se*be/q.data.BYTES_PER_ELEMENT,(se+1)*be/q.data.BYTES_PER_ELEMENT);t.compressedTexSubImage3D(i.TEXTURE_2D_ARRAY,B,0,0,se,q.width,q.height,1,ye,Ie)}}else t.compressedTexSubImage3D(i.TEXTURE_2D_ARRAY,B,0,0,0,q.width,q.height,ne.depth,ye,q.data)}else t.compressedTexImage3D(i.TEXTURE_2D_ARRAY,B,Me,q.width,q.height,ne.depth,0,q.data,0,0);else Ve("WebGLRenderer: Attempt to load unsupported compressed texture format in .uploadTexture()");else ge?U&&t.texSubImage3D(i.TEXTURE_2D_ARRAY,B,0,0,0,q.width,q.height,ne.depth,ye,He,q.data):t.texImage3D(i.TEXTURE_2D_ARRAY,B,Me,q.width,q.height,ne.depth,0,ye,He,q.data);v.layerUpdates.size>0&&v.clearLayerUpdates()}else{ge&&je&&t.texStorage2D(i.TEXTURE_2D,fe,Me,oe[0].width,oe[0].height);for(let B=0,ue=oe.length;B<ue;B++)q=oe[B],v.format!==jn?ye!==null?ge?U&&t.compressedTexSubImage2D(i.TEXTURE_2D,B,0,0,q.width,q.height,ye,q.data):t.compressedTexImage2D(i.TEXTURE_2D,B,Me,q.width,q.height,0,q.data):Ve("WebGLRenderer: Attempt to load unsupported compressed texture format in .uploadTexture()"):ge?U&&t.texSubImage2D(i.TEXTURE_2D,B,0,0,q.width,q.height,ye,He,q.data):t.texImage2D(i.TEXTURE_2D,B,Me,q.width,q.height,0,ye,He,q.data)}else if(v.isDataArrayTexture)if(ge){if(je&&t.texStorage3D(i.TEXTURE_2D_ARRAY,fe,Me,ne.width,ne.height,ne.depth),U)if(v.layerUpdates.size>0){let B=xu(ne.width,ne.height,v.format,v.type);for(let ue of v.layerUpdates){let be=ne.data.subarray(ue*B/ne.data.BYTES_PER_ELEMENT,(ue+1)*B/ne.data.BYTES_PER_ELEMENT);t.texSubImage3D(i.TEXTURE_2D_ARRAY,0,0,0,ue,ne.width,ne.height,1,ye,He,be)}v.clearLayerUpdates()}else t.texSubImage3D(i.TEXTURE_2D_ARRAY,0,0,0,0,ne.width,ne.height,ne.depth,ye,He,ne.data)}else t.texImage3D(i.TEXTURE_2D_ARRAY,0,Me,ne.width,ne.height,ne.depth,0,ye,He,ne.data);else if(v.isData3DTexture)ge?(je&&t.texStorage3D(i.TEXTURE_3D,fe,Me,ne.width,ne.height,ne.depth),U&&t.texSubImage3D(i.TEXTURE_3D,0,0,0,0,ne.width,ne.height,ne.depth,ye,He,ne.data)):t.texImage3D(i.TEXTURE_3D,0,Me,ne.width,ne.height,ne.depth,0,ye,He,ne.data);else if(v.isFramebufferTexture){if(je)if(ge)t.texStorage2D(i.TEXTURE_2D,fe,Me,ne.width,ne.height);else{let B=ne.width,ue=ne.height;for(let be=0;be<fe;be++)t.texImage2D(i.TEXTURE_2D,be,Me,B,ue,0,ye,He,null),B>>=1,ue>>=1}}else if(v.isHTMLTexture){if("texElementImage2D"in i){let B=i.canvas;if(B.hasAttribute("layoutsubtree")||B.setAttribute("layoutsubtree","true"),ne.parentNode!==B){B.appendChild(ne),u.add(v),B.onpaint=ue=>{let be=ue.changedElements;for(let se of u)be.includes(se.image)&&(se.needsUpdate=!0)},B.requestPaint();return}if(i.texElementImage2D.length===3)i.texElementImage2D(i.TEXTURE_2D,i.RGBA8,ne);else{let be=i.RGBA,se=i.RGBA,Ie=i.UNSIGNED_BYTE;i.texElementImage2D(i.TEXTURE_2D,0,be,se,Ie,ne)}i.texParameteri(i.TEXTURE_2D,i.TEXTURE_MIN_FILTER,i.LINEAR),i.texParameteri(i.TEXTURE_2D,i.TEXTURE_WRAP_S,i.CLAMP_TO_EDGE),i.texParameteri(i.TEXTURE_2D,i.TEXTURE_WRAP_T,i.CLAMP_TO_EDGE)}}else if(oe.length>0){if(ge&&je){let B=st(oe[0]);t.texStorage2D(i.TEXTURE_2D,fe,Me,B.width,B.height)}for(let B=0,ue=oe.length;B<ue;B++)q=oe[B],ge?U&&t.texSubImage2D(i.TEXTURE_2D,B,0,0,ye,He,q):t.texImage2D(i.TEXTURE_2D,B,Me,ye,He,q);v.generateMipmaps=!1}else if(ge){if(je){let B=st(ne);t.texStorage2D(i.TEXTURE_2D,fe,Me,B.width,B.height)}U&&t.texSubImage2D(i.TEXTURE_2D,0,0,0,ye,He,ne)}else t.texImage2D(i.TEXTURE_2D,0,Me,ye,He,ne);p(v)&&_(V),me.__version=pe.version,v.onUpdate&&v.onUpdate(v)}C.__version=v.version}function Ke(C,v,z){if(v.image.length!==6)return;let V=ht(C,v),Y=v.source;t.bindTexture(i.TEXTURE_CUBE_MAP,C.__webglTexture,i.TEXTURE0+z);let pe=n.get(Y);if(Y.version!==pe.__version||V===!0){t.activeTexture(i.TEXTURE0+z);let me=lt.getPrimaries(lt.workingColorSpace),Z=v.colorSpace===rs?null:lt.getPrimaries(v.colorSpace),ne=v.colorSpace===rs||me===Z?i.NONE:i.BROWSER_DEFAULT_WEBGL;t.pixelStorei(i.UNPACK_FLIP_Y_WEBGL,v.flipY),t.pixelStorei(i.UNPACK_PREMULTIPLY_ALPHA_WEBGL,v.premultiplyAlpha),t.pixelStorei(i.UNPACK_ALIGNMENT,v.unpackAlignment),t.pixelStorei(i.UNPACK_COLORSPACE_CONVERSION_WEBGL,ne);let ye=v.isCompressedTexture||v.image[0].isCompressedTexture,He=v.image[0]&&v.image[0].isDataTexture,Me=[];for(let se=0;se<6;se++)!ye&&!He?Me[se]=g(v.image[se],!0,s.maxCubemapSize):Me[se]=He?v.image[se].image:v.image[se],Me[se]=xt(v,Me[se]);let q=Me[0],oe=r.convert(v.format,v.colorSpace),ge=r.convert(v.type),je=x(v.internalFormat,oe,ge,v.normalized,v.colorSpace),U=v.isVideoTexture!==!0,fe=pe.__version===void 0||V===!0,B=Y.dataReady,ue=E(v,q);$e(i.TEXTURE_CUBE_MAP,v);let be;if(ye){U&&fe&&t.texStorage2D(i.TEXTURE_CUBE_MAP,ue,je,q.width,q.height);for(let se=0;se<6;se++){be=Me[se].mipmaps;for(let Ie=0;Ie<be.length;Ie++){let Pe=be[Ie];v.format!==jn?oe!==null?U?B&&t.compressedTexSubImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,Ie,0,0,Pe.width,Pe.height,oe,Pe.data):t.compressedTexImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,Ie,je,Pe.width,Pe.height,0,Pe.data):Ve("WebGLRenderer: Attempt to load unsupported compressed texture format in .setTextureCube()"):U?B&&t.texSubImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,Ie,0,0,Pe.width,Pe.height,oe,ge,Pe.data):t.texImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,Ie,je,Pe.width,Pe.height,0,oe,ge,Pe.data)}}}else{if(be=v.mipmaps,U&&fe){be.length>0&&ue++;let se=st(Me[0]);t.texStorage2D(i.TEXTURE_CUBE_MAP,ue,je,se.width,se.height)}for(let se=0;se<6;se++)if(He){U?B&&t.texSubImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,0,0,0,Me[se].width,Me[se].height,oe,ge,Me[se].data):t.texImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,0,je,Me[se].width,Me[se].height,0,oe,ge,Me[se].data);for(let Ie=0;Ie<be.length;Ie++){let kt=be[Ie].image[se].image;U?B&&t.texSubImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,Ie+1,0,0,kt.width,kt.height,oe,ge,kt.data):t.texImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,Ie+1,je,kt.width,kt.height,0,oe,ge,kt.data)}}else{U?B&&t.texSubImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,0,0,0,oe,ge,Me[se]):t.texImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,0,je,oe,ge,Me[se]);for(let Ie=0;Ie<be.length;Ie++){let Pe=be[Ie];U?B&&t.texSubImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,Ie+1,0,0,oe,ge,Pe.image[se]):t.texImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+se,Ie+1,je,oe,ge,Pe.image[se])}}}p(v)&&_(i.TEXTURE_CUBE_MAP),pe.__version=Y.version,v.onUpdate&&v.onUpdate(v)}C.__version=v.version}function Ae(C,v,z,V,Y,pe){let me=r.convert(z.format,z.colorSpace),Z=r.convert(z.type),ne=x(z.internalFormat,me,Z,z.normalized,z.colorSpace),ye=n.get(v),He=n.get(z);if(He.__renderTarget=v,!ye.__hasExternalTextures){let Me=Math.max(1,v.width>>pe),q=Math.max(1,v.height>>pe);Y===i.TEXTURE_3D||Y===i.TEXTURE_2D_ARRAY?t.texImage3D(Y,pe,ne,Me,q,v.depth,0,me,Z,null):t.texImage2D(Y,pe,ne,Me,q,0,me,Z,null)}t.bindFramebuffer(i.FRAMEBUFFER,C),Je(v)?o.framebufferTexture2DMultisampleEXT(i.FRAMEBUFFER,V,Y,He.__webglTexture,0,qe(v)):(Y===i.TEXTURE_2D||Y>=i.TEXTURE_CUBE_MAP_POSITIVE_X&&Y<=i.TEXTURE_CUBE_MAP_NEGATIVE_Z)&&i.framebufferTexture2D(i.FRAMEBUFFER,V,Y,He.__webglTexture,pe),t.bindFramebuffer(i.FRAMEBUFFER,null)}function ze(C,v,z){if(i.bindRenderbuffer(i.RENDERBUFFER,C),v.depthBuffer){let V=v.depthTexture,Y=V&&V.isDepthTexture?V.type:null,pe=M(v.stencilBuffer,Y),me=v.stencilBuffer?i.DEPTH_STENCIL_ATTACHMENT:i.DEPTH_ATTACHMENT;Je(v)?o.renderbufferStorageMultisampleEXT(i.RENDERBUFFER,qe(v),pe,v.width,v.height):z?i.renderbufferStorageMultisample(i.RENDERBUFFER,qe(v),pe,v.width,v.height):i.renderbufferStorage(i.RENDERBUFFER,pe,v.width,v.height),i.framebufferRenderbuffer(i.FRAMEBUFFER,me,i.RENDERBUFFER,C)}else{let V=v.textures;for(let Y=0;Y<V.length;Y++){let pe=V[Y],me=r.convert(pe.format,pe.colorSpace),Z=r.convert(pe.type),ne=x(pe.internalFormat,me,Z,pe.normalized,pe.colorSpace);Je(v)?o.renderbufferStorageMultisampleEXT(i.RENDERBUFFER,qe(v),ne,v.width,v.height):z?i.renderbufferStorageMultisample(i.RENDERBUFFER,qe(v),ne,v.width,v.height):i.renderbufferStorage(i.RENDERBUFFER,ne,v.width,v.height)}}i.bindRenderbuffer(i.RENDERBUFFER,null)}function bt(C,v,z){let V=v.isWebGLCubeRenderTarget===!0;if(t.bindFramebuffer(i.FRAMEBUFFER,C),!(v.depthTexture&&v.depthTexture.isDepthTexture))throw new Error("THREE.WebGLTextures: renderTarget.depthTexture must be an instance of THREE.DepthTexture.");let Y=n.get(v.depthTexture);if(Y.__renderTarget=v,(!Y.__webglTexture||v.depthTexture.image.width!==v.width||v.depthTexture.image.height!==v.height)&&(v.depthTexture.image.width=v.width,v.depthTexture.image.height=v.height,v.depthTexture.needsUpdate=!0),V){if(Y.__webglInit===void 0&&(Y.__webglInit=!0,v.depthTexture.addEventListener("dispose",A)),Y.__webglTexture===void 0){Y.__webglTexture=i.createTexture(),t.bindTexture(i.TEXTURE_CUBE_MAP,Y.__webglTexture),$e(i.TEXTURE_CUBE_MAP,v.depthTexture);let ye=r.convert(v.depthTexture.format),He=r.convert(v.depthTexture.type),Me;v.depthTexture.format===Ii?Me=i.DEPTH_COMPONENT24:v.depthTexture.format===Cs&&(Me=i.DEPTH24_STENCIL8);for(let q=0;q<6;q++)i.texImage2D(i.TEXTURE_CUBE_MAP_POSITIVE_X+q,0,Me,v.width,v.height,0,ye,He,null)}}else re(v.depthTexture,0);let pe=Y.__webglTexture,me=qe(v),Z=V?i.TEXTURE_CUBE_MAP_POSITIVE_X+z:i.TEXTURE_2D,ne=v.depthTexture.format===Cs?i.DEPTH_STENCIL_ATTACHMENT:i.DEPTH_ATTACHMENT;if(v.depthTexture.format===Ii)Je(v)?o.framebufferTexture2DMultisampleEXT(i.FRAMEBUFFER,ne,Z,pe,0,me):i.framebufferTexture2D(i.FRAMEBUFFER,ne,Z,pe,0);else if(v.depthTexture.format===Cs)Je(v)?o.framebufferTexture2DMultisampleEXT(i.FRAMEBUFFER,ne,Z,pe,0,me):i.framebufferTexture2D(i.FRAMEBUFFER,ne,Z,pe,0);else throw new Error("THREE.WebGLTextures: Unknown depthTexture format.")}function Q(C){let v=n.get(C),z=C.isWebGLCubeRenderTarget===!0;if(v.__boundDepthTexture!==C.depthTexture){let V=C.depthTexture;if(v.__depthDisposeCallback&&v.__depthDisposeCallback(),V){let Y=()=>{delete v.__boundDepthTexture,delete v.__depthDisposeCallback,V.removeEventListener("dispose",Y)};V.addEventListener("dispose",Y),v.__depthDisposeCallback=Y}v.__boundDepthTexture=V}if(C.depthTexture&&!v.__autoAllocateDepthBuffer)if(z)for(let V=0;V<6;V++)bt(v.__webglFramebuffer[V],C,V);else{let V=C.texture.mipmaps;V&&V.length>0?bt(v.__webglFramebuffer[0],C,0):bt(v.__webglFramebuffer,C,0)}else if(z){v.__webglDepthbuffer=[];for(let V=0;V<6;V++)if(t.bindFramebuffer(i.FRAMEBUFFER,v.__webglFramebuffer[V]),v.__webglDepthbuffer[V]===void 0)v.__webglDepthbuffer[V]=i.createRenderbuffer(),ze(v.__webglDepthbuffer[V],C,!1);else{let Y=C.stencilBuffer?i.DEPTH_STENCIL_ATTACHMENT:i.DEPTH_ATTACHMENT,pe=v.__webglDepthbuffer[V];i.bindRenderbuffer(i.RENDERBUFFER,pe),i.framebufferRenderbuffer(i.FRAMEBUFFER,Y,i.RENDERBUFFER,pe)}}else{let V=C.texture.mipmaps;if(V&&V.length>0?t.bindFramebuffer(i.FRAMEBUFFER,v.__webglFramebuffer[0]):t.bindFramebuffer(i.FRAMEBUFFER,v.__webglFramebuffer),v.__webglDepthbuffer===void 0)v.__webglDepthbuffer=i.createRenderbuffer(),ze(v.__webglDepthbuffer,C,!1);else{let Y=C.stencilBuffer?i.DEPTH_STENCIL_ATTACHMENT:i.DEPTH_ATTACHMENT,pe=v.__webglDepthbuffer;i.bindRenderbuffer(i.RENDERBUFFER,pe),i.framebufferRenderbuffer(i.FRAMEBUFFER,Y,i.RENDERBUFFER,pe)}}t.bindFramebuffer(i.FRAMEBUFFER,null)}function ce(C,v,z){let V=n.get(C);v!==void 0&&Ae(V.__webglFramebuffer,C,C.texture,i.COLOR_ATTACHMENT0,i.TEXTURE_2D,0),z!==void 0&&Q(C)}function le(C){let v=C.texture,z=n.get(C),V=n.get(v);C.addEventListener("dispose",y);let Y=C.textures,pe=C.isWebGLCubeRenderTarget===!0,me=Y.length>1;if(me||(V.__webglTexture===void 0&&(V.__webglTexture=i.createTexture()),V.__version=v.version,a.memory.textures++),pe){z.__webglFramebuffer=[];for(let Z=0;Z<6;Z++)if(v.mipmaps&&v.mipmaps.length>0){z.__webglFramebuffer[Z]=[];for(let ne=0;ne<v.mipmaps.length;ne++)z.__webglFramebuffer[Z][ne]=i.createFramebuffer()}else z.__webglFramebuffer[Z]=i.createFramebuffer()}else{if(v.mipmaps&&v.mipmaps.length>0){z.__webglFramebuffer=[];for(let Z=0;Z<v.mipmaps.length;Z++)z.__webglFramebuffer[Z]=i.createFramebuffer()}else z.__webglFramebuffer=i.createFramebuffer();if(me)for(let Z=0,ne=Y.length;Z<ne;Z++){let ye=n.get(Y[Z]);ye.__webglTexture===void 0&&(ye.__webglTexture=i.createTexture(),a.memory.textures++)}if(C.samples>0&&Je(C)===!1){z.__webglMultisampledFramebuffer=i.createFramebuffer(),z.__webglColorRenderbuffer=[],t.bindFramebuffer(i.FRAMEBUFFER,z.__webglMultisampledFramebuffer);for(let Z=0;Z<Y.length;Z++){let ne=Y[Z];z.__webglColorRenderbuffer[Z]=i.createRenderbuffer(),i.bindRenderbuffer(i.RENDERBUFFER,z.__webglColorRenderbuffer[Z]);let ye=r.convert(ne.format,ne.colorSpace),He=r.convert(ne.type),Me=x(ne.internalFormat,ye,He,ne.normalized,ne.colorSpace,C.isXRRenderTarget===!0),q=qe(C);i.renderbufferStorageMultisample(i.RENDERBUFFER,q,Me,C.width,C.height),i.framebufferRenderbuffer(i.FRAMEBUFFER,i.COLOR_ATTACHMENT0+Z,i.RENDERBUFFER,z.__webglColorRenderbuffer[Z])}i.bindRenderbuffer(i.RENDERBUFFER,null),C.depthBuffer&&(z.__webglDepthRenderbuffer=i.createRenderbuffer(),ze(z.__webglDepthRenderbuffer,C,!0)),t.bindFramebuffer(i.FRAMEBUFFER,null)}}if(pe){t.bindTexture(i.TEXTURE_CUBE_MAP,V.__webglTexture),$e(i.TEXTURE_CUBE_MAP,v);for(let Z=0;Z<6;Z++)if(v.mipmaps&&v.mipmaps.length>0)for(let ne=0;ne<v.mipmaps.length;ne++)Ae(z.__webglFramebuffer[Z][ne],C,v,i.COLOR_ATTACHMENT0,i.TEXTURE_CUBE_MAP_POSITIVE_X+Z,ne);else Ae(z.__webglFramebuffer[Z],C,v,i.COLOR_ATTACHMENT0,i.TEXTURE_CUBE_MAP_POSITIVE_X+Z,0);p(v)&&_(i.TEXTURE_CUBE_MAP),t.unbindTexture()}else if(me){for(let Z=0,ne=Y.length;Z<ne;Z++){let ye=Y[Z],He=n.get(ye),Me=i.TEXTURE_2D;(C.isWebGL3DRenderTarget||C.isWebGLArrayRenderTarget)&&(Me=C.isWebGL3DRenderTarget?i.TEXTURE_3D:i.TEXTURE_2D_ARRAY),t.bindTexture(Me,He.__webglTexture),$e(Me,ye),Ae(z.__webglFramebuffer,C,ye,i.COLOR_ATTACHMENT0+Z,Me,0),p(ye)&&_(Me)}t.unbindTexture()}else{let Z=i.TEXTURE_2D;if((C.isWebGL3DRenderTarget||C.isWebGLArrayRenderTarget)&&(Z=C.isWebGL3DRenderTarget?i.TEXTURE_3D:i.TEXTURE_2D_ARRAY),t.bindTexture(Z,V.__webglTexture),$e(Z,v),v.mipmaps&&v.mipmaps.length>0)for(let ne=0;ne<v.mipmaps.length;ne++)Ae(z.__webglFramebuffer[ne],C,v,i.COLOR_ATTACHMENT0,Z,ne);else Ae(z.__webglFramebuffer,C,v,i.COLOR_ATTACHMENT0,Z,0);p(v)&&_(Z),t.unbindTexture()}C.depthBuffer&&Q(C)}function de(C){let v=C.textures;for(let z=0,V=v.length;z<V;z++){let Y=v[z];if(p(Y)){let pe=S(C),me=n.get(Y).__webglTexture;t.bindTexture(pe,me),_(pe),t.unbindTexture()}}}let xe=[],Xe=[];function Fe(C){if(C.samples>0){if(Je(C)===!1){let v=C.textures,z=C.width,V=C.height,Y=i.COLOR_BUFFER_BIT,pe=C.stencilBuffer?i.DEPTH_STENCIL_ATTACHMENT:i.DEPTH_ATTACHMENT,me=n.get(C),Z=v.length>1;if(Z)for(let ye=0;ye<v.length;ye++)t.bindFramebuffer(i.FRAMEBUFFER,me.__webglMultisampledFramebuffer),i.framebufferRenderbuffer(i.FRAMEBUFFER,i.COLOR_ATTACHMENT0+ye,i.RENDERBUFFER,null),t.bindFramebuffer(i.FRAMEBUFFER,me.__webglFramebuffer),i.framebufferTexture2D(i.DRAW_FRAMEBUFFER,i.COLOR_ATTACHMENT0+ye,i.TEXTURE_2D,null,0);t.bindFramebuffer(i.READ_FRAMEBUFFER,me.__webglMultisampledFramebuffer);let ne=C.texture.mipmaps;ne&&ne.length>0?t.bindFramebuffer(i.DRAW_FRAMEBUFFER,me.__webglFramebuffer[0]):t.bindFramebuffer(i.DRAW_FRAMEBUFFER,me.__webglFramebuffer);for(let ye=0;ye<v.length;ye++){if(C.resolveDepthBuffer&&(C.depthBuffer&&(Y|=i.DEPTH_BUFFER_BIT),C.stencilBuffer&&C.resolveStencilBuffer&&(Y|=i.STENCIL_BUFFER_BIT)),Z){i.framebufferRenderbuffer(i.READ_FRAMEBUFFER,i.COLOR_ATTACHMENT0,i.RENDERBUFFER,me.__webglColorRenderbuffer[ye]);let He=n.get(v[ye]).__webglTexture;i.framebufferTexture2D(i.DRAW_FRAMEBUFFER,i.COLOR_ATTACHMENT0,i.TEXTURE_2D,He,0)}i.blitFramebuffer(0,0,z,V,0,0,z,V,Y,i.NEAREST),c===!0&&(xe.length=0,Xe.length=0,xe.push(i.COLOR_ATTACHMENT0+ye),C.depthBuffer&&C.storeMultisampledDepthBuffer===!1&&(xe.push(pe),Xe.push(pe),i.invalidateFramebuffer(i.DRAW_FRAMEBUFFER,Xe)),i.invalidateFramebuffer(i.READ_FRAMEBUFFER,xe))}if(t.bindFramebuffer(i.READ_FRAMEBUFFER,null),t.bindFramebuffer(i.DRAW_FRAMEBUFFER,null),Z)for(let ye=0;ye<v.length;ye++){t.bindFramebuffer(i.FRAMEBUFFER,me.__webglMultisampledFramebuffer),i.framebufferRenderbuffer(i.FRAMEBUFFER,i.COLOR_ATTACHMENT0+ye,i.RENDERBUFFER,me.__webglColorRenderbuffer[ye]);let He=n.get(v[ye]).__webglTexture;t.bindFramebuffer(i.FRAMEBUFFER,me.__webglFramebuffer),i.framebufferTexture2D(i.DRAW_FRAMEBUFFER,i.COLOR_ATTACHMENT0+ye,i.TEXTURE_2D,He,0)}t.bindFramebuffer(i.DRAW_FRAMEBUFFER,me.__webglMultisampledFramebuffer)}else if(C.depthBuffer&&C.storeMultisampledDepthBuffer===!1&&c){let v=C.stencilBuffer?i.DEPTH_STENCIL_ATTACHMENT:i.DEPTH_ATTACHMENT;i.invalidateFramebuffer(i.DRAW_FRAMEBUFFER,[v])}}}function qe(C){return Math.min(s.maxSamples,C.samples)}function Je(C){let v=n.get(C);return C.samples>0&&e.has("WEBGL_multisampled_render_to_texture")===!0&&v.__useRenderToTexture!==!1}function N(C){let v=a.render.frame;h.get(C)!==v&&(h.set(C,v),C.update())}function xt(C,v){let z=C.colorSpace,V=C.format,Y=C.type;return C.isCompressedTexture===!0||C.isVideoTexture===!0||z!==Dn&&z!==rs&&(lt.getTransfer(z)===wt?(V!==jn||Y!==Un)&&Ve("WebGLTextures: sRGB encoded textures have to use RGBAFormat and UnsignedByteType."):Ye("WebGLTextures: Unsupported texture color space:",z)),v}function st(C){return typeof HTMLImageElement<"u"&&C instanceof HTMLImageElement?(l.width=C.naturalWidth||C.width,l.height=C.naturalHeight||C.height):typeof VideoFrame<"u"&&C instanceof VideoFrame?(l.width=C.displayWidth,l.height=C.displayHeight):(l.width=C.width,l.height=C.height),l}this.allocateTextureUnit=W,this.resetTextureUnits=F,this.getTextureUnits=D,this.setTextureUnits=k,this.setTexture2D=re,this.setTexture2DArray=j,this.setTexture3D=ee,this.setTextureCube=te,this.rebindTextures=ce,this.setupRenderTarget=le,this.updateRenderTargetMipmap=de,this.updateMultisampleRenderTarget=Fe,this.setupDepthRenderbuffer=Q,this.setupFrameBufferTexture=Ae,this.useMultisampledRTT=Je,this.isReversedDepthBuffer=function(){return t.buffers.depth.getReversed()}}function cy(i,e){function t(n,s=rs){let r,a=lt.getTransfer(s);if(n===Un)return i.UNSIGNED_BYTE;if(n===Hc)return i.UNSIGNED_SHORT_4_4_4_4;if(n===Gc)return i.UNSIGNED_SHORT_5_5_5_1;if(n===au)return i.UNSIGNED_INT_5_9_9_9_REV;if(n===ou)return i.UNSIGNED_INT_10F_11F_11F_REV;if(n===su)return i.BYTE;if(n===ru)return i.SHORT;if(n===Yr)return i.UNSIGNED_SHORT;if(n===zc)return i.INT;if(n===Mi)return i.UNSIGNED_INT;if(n===Xn)return i.FLOAT;if(n===Si)return i.HALF_FLOAT;if(n===cu)return i.ALPHA;if(n===lu)return i.RGB;if(n===jn)return i.RGBA;if(n===Ii)return i.DEPTH_COMPONENT;if(n===Cs)return i.DEPTH_STENCIL;if(n===Vc)return i.RED;if(n===Wc)return i.RED_INTEGER;if(n===Ps)return i.RG;if(n===qc)return i.RG_INTEGER;if(n===Xc)return i.RGBA_INTEGER;if(n===no||n===io||n===so||n===ro)if(a===wt)if(r=e.get("WEBGL_compressed_texture_s3tc_srgb"),r!==null){if(n===no)return r.COMPRESSED_SRGB_S3TC_DXT1_EXT;if(n===io)return r.COMPRESSED_SRGB_ALPHA_S3TC_DXT1_EXT;if(n===so)return r.COMPRESSED_SRGB_ALPHA_S3TC_DXT3_EXT;if(n===ro)return r.COMPRESSED_SRGB_ALPHA_S3TC_DXT5_EXT}else return null;else if(r=e.get("WEBGL_compressed_texture_s3tc"),r!==null){if(n===no)return r.COMPRESSED_RGB_S3TC_DXT1_EXT;if(n===io)return r.COMPRESSED_RGBA_S3TC_DXT1_EXT;if(n===so)return r.COMPRESSED_RGBA_S3TC_DXT3_EXT;if(n===ro)return r.COMPRESSED_RGBA_S3TC_DXT5_EXT}else return null;if(n===jc||n===Kc||n===Yc||n===Jc)if(r=e.get("WEBGL_compressed_texture_pvrtc"),r!==null){if(n===jc)return r.COMPRESSED_RGB_PVRTC_4BPPV1_IMG;if(n===Kc)return r.COMPRESSED_RGB_PVRTC_2BPPV1_IMG;if(n===Yc)return r.COMPRESSED_RGBA_PVRTC_4BPPV1_IMG;if(n===Jc)return r.COMPRESSED_RGBA_PVRTC_2BPPV1_IMG}else return null;if(n===Zc||n===$c||n===Qc||n===el||n===tl||n===ao||n===nl)if(r=e.get("WEBGL_compressed_texture_etc"),r!==null){if(n===Zc||n===$c)return a===wt?r.COMPRESSED_SRGB8_ETC2:r.COMPRESSED_RGB8_ETC2;if(n===Qc)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ETC2_EAC:r.COMPRESSED_RGBA8_ETC2_EAC;if(n===el)return r.COMPRESSED_R11_EAC;if(n===tl)return r.COMPRESSED_SIGNED_R11_EAC;if(n===ao)return r.COMPRESSED_RG11_EAC;if(n===nl)return r.COMPRESSED_SIGNED_RG11_EAC}else return null;if(n===il||n===sl||n===rl||n===al||n===ol||n===cl||n===ll||n===hl||n===ul||n===dl||n===fl||n===pl||n===ml||n===gl)if(r=e.get("WEBGL_compressed_texture_astc"),r!==null){if(n===il)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_4x4_KHR:r.COMPRESSED_RGBA_ASTC_4x4_KHR;if(n===sl)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_5x4_KHR:r.COMPRESSED_RGBA_ASTC_5x4_KHR;if(n===rl)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_5x5_KHR:r.COMPRESSED_RGBA_ASTC_5x5_KHR;if(n===al)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_6x5_KHR:r.COMPRESSED_RGBA_ASTC_6x5_KHR;if(n===ol)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_6x6_KHR:r.COMPRESSED_RGBA_ASTC_6x6_KHR;if(n===cl)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_8x5_KHR:r.COMPRESSED_RGBA_ASTC_8x5_KHR;if(n===ll)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_8x6_KHR:r.COMPRESSED_RGBA_ASTC_8x6_KHR;if(n===hl)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_8x8_KHR:r.COMPRESSED_RGBA_ASTC_8x8_KHR;if(n===ul)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_10x5_KHR:r.COMPRESSED_RGBA_ASTC_10x5_KHR;if(n===dl)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_10x6_KHR:r.COMPRESSED_RGBA_ASTC_10x6_KHR;if(n===fl)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_10x8_KHR:r.COMPRESSED_RGBA_ASTC_10x8_KHR;if(n===pl)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_10x10_KHR:r.COMPRESSED_RGBA_ASTC_10x10_KHR;if(n===ml)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_12x10_KHR:r.COMPRESSED_RGBA_ASTC_12x10_KHR;if(n===gl)return a===wt?r.COMPRESSED_SRGB8_ALPHA8_ASTC_12x12_KHR:r.COMPRESSED_RGBA_ASTC_12x12_KHR}else return null;if(n===bl||n===xl||n===_l)if(r=e.get("EXT_texture_compression_bptc"),r!==null){if(n===bl)return a===wt?r.COMPRESSED_SRGB_ALPHA_BPTC_UNORM_EXT:r.COMPRESSED_RGBA_BPTC_UNORM_EXT;if(n===xl)return r.COMPRESSED_RGB_BPTC_SIGNED_FLOAT_EXT;if(n===_l)return r.COMPRESSED_RGB_BPTC_UNSIGNED_FLOAT_EXT}else return null;if(n===yl||n===vl||n===oo||n===Ml)if(r=e.get("EXT_texture_compression_rgtc"),r!==null){if(n===yl)return r.COMPRESSED_RED_RGTC1_EXT;if(n===vl)return r.COMPRESSED_SIGNED_RED_RGTC1_EXT;if(n===oo)return r.COMPRESSED_RED_GREEN_RGTC2_EXT;if(n===Ml)return r.COMPRESSED_SIGNED_RED_GREEN_RGTC2_EXT}else return null;return n===Jr?i.UNSIGNED_INT_24_8:i[n]!==void 0?i[n]:null}return{convert:t}}var ly=`
void main() {

	gl_Position = vec4( position, 1.0 );

}`,hy=`
uniform sampler2DArray depthColor;
uniform float depthWidth;
uniform float depthHeight;

void main() {

	vec2 coord = vec2( gl_FragCoord.x / depthWidth, gl_FragCoord.y / depthHeight );

	if ( coord.x >= 1.0 ) {

		gl_FragDepth = texture( depthColor, vec3( coord.x - 1.0, coord.y, 1 ) ).r;

	} else {

		gl_FragDepth = texture( depthColor, vec3( coord.x, coord.y, 0 ) ).r;

	}

}`,Lu=class{constructor(){this.texture=null,this.mesh=null,this.depthNear=0,this.depthFar=0}init(e,t){if(this.texture===null){let n=new Fa(e.texture);(e.depthNear!==t.depthNear||e.depthFar!==t.depthFar)&&(this.depthNear=e.depthNear,this.depthFar=e.depthFar),this.texture=n}}getMesh(e){if(this.texture!==null&&this.mesh===null){let t=e.cameras[0].viewport,n=new Wn({vertexShader:ly,fragmentShader:hy,uniforms:{depthColor:{value:this.texture},depthWidth:{value:t.z},depthHeight:{value:t.w}}});this.mesh=new ke(new Vn(20,20),n)}return this.mesh}reset(){this.texture=null,this.mesh=null}getDepthTexture(){return this.texture}},Du=class extends _i{constructor(e,t){super();let n=this,s=null,r=1,a=null,o="local-floor",c=1,l=null,h=null,u=null,d=null,f=null,m=null,b=typeof XRWebGLBinding<"u",g=new Lu,p={},_=t.getContextAttributes(),S=null,x=null,M=[],E=[],A=new ae,y=null,w=null,R=new Yt;R.viewport=new Pt;let I=new Yt;I.viewport=new Pt;let L=[R,I],F=new Nc,D=null,k=null;this.cameraAutoUpdate=!0,this.enabled=!1,this.isPresenting=!1,this.getController=function(J){let ie=M[J];return ie===void 0&&(ie=new Nr,M[J]=ie),ie.getTargetRaySpace()},this.getControllerGrip=function(J){let ie=M[J];return ie===void 0&&(ie=new Nr,M[J]=ie),ie.getGripSpace()},this.getHand=function(J){let ie=M[J];return ie===void 0&&(ie=new Nr,M[J]=ie),ie.getHandSpace()};function W(J){let ie=E.indexOf(J.inputSource);if(ie===-1)return;let ve=M[ie];ve!==void 0&&(ve.update(J.inputSource,J.frame,l||a),ve.dispatchEvent({type:J.type,data:J.inputSource}))}function X(){s.removeEventListener("select",W),s.removeEventListener("selectstart",W),s.removeEventListener("selectend",W),s.removeEventListener("squeeze",W),s.removeEventListener("squeezestart",W),s.removeEventListener("squeezeend",W),s.removeEventListener("end",X),s.removeEventListener("inputsourceschange",re);for(let J=0;J<M.length;J++){let ie=E[J];ie!==null&&(E[J]=null,M[J].disconnect(ie))}D=null,k=null,g.reset();for(let J in p)delete p[J];if(e.setRenderTarget(S),f=null,d=null,u=null,s=null,x=null,ht.stop(),n.isPresenting=!1,e.setPixelRatio(y),e.setSize(A.width,A.height,!1),w!==null){let J=w.camera;J.fov=w.fov,J.zoom=w.zoom,J.updateProjectionMatrix(),w=null}n.dispatchEvent({type:"sessionend"})}this.setFramebufferScaleFactor=function(J){r=J,n.isPresenting===!0&&Ve("WebXRManager: Cannot change framebuffer scale while presenting.")},this.setReferenceSpaceType=function(J){o=J,n.isPresenting===!0&&Ve("WebXRManager: Cannot change reference space type while presenting.")},this.getReferenceSpace=function(){return l||a},this.setReferenceSpace=function(J){l=J},this.getBaseLayer=function(){return d!==null?d:f},this.getBinding=function(){return u===null&&b&&(u=new XRWebGLBinding(s,t)),u},this.getFrame=function(){return m},this.getSession=function(){return s},this.setSession=async function(J){if(s=J,s!==null){if(S=e.getRenderTarget(),s.addEventListener("select",W),s.addEventListener("selectstart",W),s.addEventListener("selectend",W),s.addEventListener("squeeze",W),s.addEventListener("squeezestart",W),s.addEventListener("squeezeend",W),s.addEventListener("end",X),s.addEventListener("inputsourceschange",re),_.xrCompatible!==!0&&await t.makeXRCompatible(),y=e.getPixelRatio(),e.getSize(A),b&&"createProjectionLayer"in XRWebGLBinding.prototype){let ve=null,Ke=null,Ae=null;_.depth&&(Ae=_.stencil?t.DEPTH24_STENCIL8:t.DEPTH_COMPONENT24,ve=_.stencil?Cs:Ii,Ke=_.stencil?Jr:Mi);let ze={colorFormat:t.RGBA8,depthFormat:Ae,scaleFactor:r};u=this.getBinding(),d=u.createProjectionLayer(ze),s.updateRenderState({layers:[d]}),e.setPixelRatio(1),e.setSize(d.textureWidth,d.textureHeight,!1),x=new wn(d.textureWidth,d.textureHeight,{format:jn,type:Un,depthTexture:new _s(d.textureWidth,d.textureHeight,Ke,void 0,void 0,void 0,void 0,void 0,void 0,ve),stencilBuffer:_.stencil,colorSpace:e.outputColorSpace,samples:_.antialias?4:0,resolveDepthBuffer:d.ignoreDepthValues===!1,resolveStencilBuffer:d.ignoreDepthValues===!1,storeMultisampledDepthBuffer:d.ignoreDepthValues===!1,storeMultisampledStencilBuffer:d.ignoreDepthValues===!1})}else{let ve={antialias:_.antialias,alpha:!0,depth:_.depth,stencil:_.stencil,framebufferScaleFactor:r};f=new XRWebGLLayer(s,t,ve),s.updateRenderState({baseLayer:f}),e.setPixelRatio(1),e.setSize(f.framebufferWidth,f.framebufferHeight,!1),x=new wn(f.framebufferWidth,f.framebufferHeight,{format:jn,type:Un,colorSpace:e.outputColorSpace,stencilBuffer:_.stencil,resolveDepthBuffer:f.ignoreDepthValues===!1,resolveStencilBuffer:f.ignoreDepthValues===!1,storeMultisampledDepthBuffer:f.ignoreDepthValues===!1,storeMultisampledStencilBuffer:f.ignoreDepthValues===!1})}x.isXRRenderTarget=!0,this.setFoveation(c),l=null,a=await s.requestReferenceSpace(o),ht.setContext(s),ht.start(),n.isPresenting=!0,n.dispatchEvent({type:"sessionstart"})}},this.getEnvironmentBlendMode=function(){if(s!==null)return s.environmentBlendMode},this.getDepthTexture=function(){return g.getDepthTexture()};function re(J){for(let ie=0;ie<J.removed.length;ie++){let ve=J.removed[ie],Ke=E.indexOf(ve);Ke>=0&&(E[Ke]=null,M[Ke].disconnect(ve))}for(let ie=0;ie<J.added.length;ie++){let ve=J.added[ie],Ke=E.indexOf(ve);if(Ke===-1){for(let ze=0;ze<M.length;ze++)if(ze>=E.length){E.push(ve),Ke=ze;break}else if(E[ze]===null){E[ze]=ve,Ke=ze;break}if(Ke===-1)break}let Ae=M[Ke];Ae&&Ae.connect(ve)}}let j=new P,ee=new P;function te(J,ie,ve){j.setFromMatrixPosition(ie.matrixWorld),ee.setFromMatrixPosition(ve.matrixWorld);let Ke=j.distanceTo(ee),Ae=ie.projectionMatrix.elements,ze=ve.projectionMatrix.elements,bt=Ae[14]/(Ae[10]-1),Q=Ae[14]/(Ae[10]+1),ce=(Ae[9]+1)/Ae[5],le=(Ae[9]-1)/Ae[5],de=(Ae[8]-1)/Ae[0],xe=(ze[8]+1)/ze[0],Xe=bt*de,Fe=bt*xe,qe=Ke/(-de+xe),Je=qe*-de;if(ie.matrixWorld.decompose(J.position,J.quaternion,J.scale),J.translateX(Je),J.translateZ(qe),J.matrixWorld.compose(J.position,J.quaternion,J.scale),J.matrixWorldInverse.copy(J.matrixWorld).invert(),Ae[10]===-1)J.projectionMatrix.copy(ie.projectionMatrix),J.projectionMatrixInverse.copy(ie.projectionMatrixInverse);else{let N=bt+qe,xt=Q+qe,st=Xe-Je,C=Fe+(Ke-Je),v=ce*Q/xt*N,z=le*Q/xt*N;J.projectionMatrix.makePerspective(st,C,v,z,N,xt),J.projectionMatrixInverse.copy(J.projectionMatrix).invert()}}function Ne(J,ie){ie===null?J.matrixWorld.copy(J.matrix):J.matrixWorld.multiplyMatrices(ie.matrixWorld,J.matrix),J.matrixWorldInverse.copy(J.matrixWorld).invert()}this.updateCamera=function(J){if(s===null)return;let ie=J.near,ve=J.far;g.texture!==null&&(g.depthNear>0&&(ie=g.depthNear),g.depthFar>0&&(ve=g.depthFar)),F.near=I.near=R.near=ie,F.far=I.far=R.far=ve,(D!==F.near||k!==F.far)&&(s.updateRenderState({depthNear:F.near,depthFar:F.far}),D=F.near,k=F.far),F.layers.mask=J.layers.mask|6,R.layers.mask=F.layers.mask&-5,I.layers.mask=F.layers.mask&-3;let Ke=J.parent,Ae=F.cameras;Ne(F,Ke);for(let ze=0;ze<Ae.length;ze++)Ne(Ae[ze],Ke);Ae.length===2?te(F,R,I):F.projectionMatrix.copy(R.projectionMatrix),w===null&&J.isPerspectiveCamera&&(w={camera:J,fov:J.fov,zoom:J.zoom}),Se(J,F,Ke)};function Se(J,ie,ve){ve===null?J.matrix.copy(ie.matrixWorld):(J.matrix.copy(ve.matrixWorld),J.matrix.invert(),J.matrix.multiply(ie.matrixWorld)),J.matrix.decompose(J.position,J.quaternion,J.scale),J.updateMatrixWorld(!0),J.projectionMatrix.copy(ie.projectionMatrix),J.projectionMatrixInverse.copy(ie.projectionMatrixInverse),J.isPerspectiveCamera&&(J.fov=qs*2*Math.atan(1/J.projectionMatrix.elements[5]),J.zoom=1)}this.getCamera=function(){return F},this.getFoveation=function(){if(!(d===null&&f===null))return c},this.setFoveation=function(J){c=J,d!==null&&(d.fixedFoveation=J),f!==null&&f.fixedFoveation!==void 0&&(f.fixedFoveation=J)},this.hasDepthSensing=function(){return g.texture!==null},this.getDepthSensingMesh=function(){return g.getMesh(F)},this.getCameraTexture=function(J){return p[J]};let ct=null;function $e(J,ie){if(h=ie.getViewerPose(l||a),m=ie,h!==null){let ve=h.views;f!==null&&(e.setRenderTargetFramebuffer(x,f.framebuffer),e.setRenderTarget(x));let Ke=!1;ve.length!==F.cameras.length&&(F.cameras.length=0,Ke=!0);for(let Q=0;Q<ve.length;Q++){let ce=ve[Q],le=null;if(f!==null)le=f.getViewport(ce);else{let xe=u.getViewSubImage(d,ce);le=xe.viewport,Q===0&&(e.setRenderTargetTextures(x,xe.colorTexture,xe.depthStencilTexture),e.setRenderTarget(x))}let de=L[Q];de===void 0&&(de=new Yt,de.layers.enable(Q),de.viewport=new Pt,L[Q]=de),de.matrix.fromArray(ce.transform.matrix),de.matrix.decompose(de.position,de.quaternion,de.scale),de.projectionMatrix.fromArray(ce.projectionMatrix),de.projectionMatrixInverse.copy(de.projectionMatrix).invert(),de.viewport.set(le.x,le.y,le.width,le.height),Q===0&&(F.matrix.copy(de.matrix),F.matrix.decompose(F.position,F.quaternion,F.scale)),Ke===!0&&F.cameras.push(de)}let Ae=s.enabledFeatures;if(Ae&&Ae.includes("depth-sensing")&&s.depthUsage=="gpu-optimized"&&b){u=n.getBinding();let Q=u.getDepthInformation(ve[0]);Q&&Q.isValid&&Q.texture&&g.init(Q,s.renderState)}if(Ae&&Ae.includes("camera-access")&&b){e.state.unbindTexture(),u=n.getBinding();for(let Q=0;Q<ve.length;Q++){let ce=ve[Q].camera;if(ce){let le=p[ce];le||(le=new Fa,p[ce]=le);let de=u.getCameraImage(ce);le.sourceTexture=de}}}}for(let ve=0;ve<M.length;ve++){let Ke=E[ve],Ae=M[ve];Ke!==null&&Ae!==void 0&&Ae.update(Ke,ie,l||a)}ct&&ct(J,ie),ie.detectedPlanes&&n.dispatchEvent({type:"planesdetected",data:ie}),m=null}let ht=new jp;ht.setAnimationLoop($e),this.setAnimationLoop=function(J){ct=J},this.dispose=function(){}}},uy=new Ze,Qp=new et;Qp.set(-1,0,0,0,1,0,0,0,1);function dy(i,e){function t(g,p){g.matrixAutoUpdate===!0&&g.updateMatrix(),p.value.copy(g.matrix)}function n(g,p){p.color.getRGB(g.fogColor.value,mu(i)),p.isFog?(g.fogNear.value=p.near,g.fogFar.value=p.far):p.isFogExp2&&(g.fogDensity.value=p.density)}function s(g,p,_,S,x){p.isNodeMaterial?p.uniformsNeedUpdate=!1:p.isMeshBasicMaterial?r(g,p):p.isMeshLambertMaterial?(r(g,p),p.envMap&&(g.envMapIntensity.value=p.envMapIntensity)):p.isMeshToonMaterial?(r(g,p),u(g,p)):p.isMeshPhongMaterial?(r(g,p),h(g,p),p.envMap&&(g.envMapIntensity.value=p.envMapIntensity)):p.isMeshStandardMaterial?(r(g,p),d(g,p),p.isMeshPhysicalMaterial&&f(g,p,x)):p.isMeshMatcapMaterial?(r(g,p),m(g,p)):p.isMeshDepthMaterial?r(g,p):p.isMeshDistanceMaterial?(r(g,p),b(g,p)):p.isMeshNormalMaterial?r(g,p):p.isLineBasicMaterial?(a(g,p),p.isLineDashedMaterial&&o(g,p)):p.isPointsMaterial?c(g,p,_,S):p.isSpriteMaterial?l(g,p):p.isShadowMaterial?(g.color.value.copy(p.color),g.opacity.value=p.opacity):p.isShaderMaterial&&(p.uniformsNeedUpdate=!1)}function r(g,p){g.opacity.value=p.opacity,p.color&&g.diffuse.value.copy(p.color),p.emissive&&g.emissive.value.copy(p.emissive).multiplyScalar(p.emissiveIntensity),p.map&&(g.map.value=p.map,t(p.map,g.mapTransform)),p.alphaMap&&(g.alphaMap.value=p.alphaMap,t(p.alphaMap,g.alphaMapTransform)),p.bumpMap&&(g.bumpMap.value=p.bumpMap,t(p.bumpMap,g.bumpMapTransform),g.bumpScale.value=p.bumpScale,p.side===yn&&(g.bumpScale.value*=-1)),p.normalMap&&(g.normalMap.value=p.normalMap,t(p.normalMap,g.normalMapTransform),g.normalScale.value.copy(p.normalScale),p.side===yn&&g.normalScale.value.negate()),p.displacementMap&&(g.displacementMap.value=p.displacementMap,t(p.displacementMap,g.displacementMapTransform),g.displacementScale.value=p.displacementScale,g.displacementBias.value=p.displacementBias),p.emissiveMap&&(g.emissiveMap.value=p.emissiveMap,t(p.emissiveMap,g.emissiveMapTransform)),p.specularMap&&(g.specularMap.value=p.specularMap,t(p.specularMap,g.specularMapTransform)),p.alphaTest>0&&(g.alphaTest.value=p.alphaTest);let _=e.get(p),S=_.envMap,x=_.envMapRotation;S&&(g.envMap.value=S,g.envMapRotation.value.setFromMatrix4(uy.makeRotationFromEuler(x)).transpose(),S.isCubeTexture&&S.isRenderTargetTexture===!1&&g.envMapRotation.value.premultiply(Qp),g.reflectivity.value=p.reflectivity,g.ior.value=p.ior,g.refractionRatio.value=p.refractionRatio),p.lightMap&&(g.lightMap.value=p.lightMap,g.lightMapIntensity.value=p.lightMapIntensity,t(p.lightMap,g.lightMapTransform)),p.aoMap&&(g.aoMap.value=p.aoMap,g.aoMapIntensity.value=p.aoMapIntensity,t(p.aoMap,g.aoMapTransform))}function a(g,p){g.diffuse.value.copy(p.color),g.opacity.value=p.opacity,p.map&&(g.map.value=p.map,t(p.map,g.mapTransform))}function o(g,p){g.dashSize.value=p.dashSize,g.totalSize.value=p.dashSize+p.gapSize,g.scale.value=p.scale}function c(g,p,_,S){g.diffuse.value.copy(p.color),g.opacity.value=p.opacity,g.size.value=p.size*_,g.scale.value=S*.5,p.map&&(g.map.value=p.map,t(p.map,g.uvTransform)),p.alphaMap&&(g.alphaMap.value=p.alphaMap,t(p.alphaMap,g.alphaMapTransform)),p.alphaTest>0&&(g.alphaTest.value=p.alphaTest)}function l(g,p){g.diffuse.value.copy(p.color),g.opacity.value=p.opacity,g.rotation.value=p.rotation,p.map&&(g.map.value=p.map,t(p.map,g.mapTransform)),p.alphaMap&&(g.alphaMap.value=p.alphaMap,t(p.alphaMap,g.alphaMapTransform)),p.alphaTest>0&&(g.alphaTest.value=p.alphaTest)}function h(g,p){g.specular.value.copy(p.specular),g.shininess.value=Math.max(p.shininess,1e-4)}function u(g,p){p.gradientMap&&(g.gradientMap.value=p.gradientMap)}function d(g,p){g.metalness.value=p.metalness,p.metalnessMap&&(g.metalnessMap.value=p.metalnessMap,t(p.metalnessMap,g.metalnessMapTransform)),g.roughness.value=p.roughness,p.roughnessMap&&(g.roughnessMap.value=p.roughnessMap,t(p.roughnessMap,g.roughnessMapTransform)),p.envMap&&(g.envMapIntensity.value=p.envMapIntensity)}function f(g,p,_){g.ior.value=p.ior,p.sheen>0&&(g.sheenColor.value.copy(p.sheenColor).multiplyScalar(p.sheen),g.sheenRoughness.value=p.sheenRoughness,p.sheenColorMap&&(g.sheenColorMap.value=p.sheenColorMap,t(p.sheenColorMap,g.sheenColorMapTransform)),p.sheenRoughnessMap&&(g.sheenRoughnessMap.value=p.sheenRoughnessMap,t(p.sheenRoughnessMap,g.sheenRoughnessMapTransform))),p.clearcoat>0&&(g.clearcoat.value=p.clearcoat,g.clearcoatRoughness.value=p.clearcoatRoughness,p.clearcoatMap&&(g.clearcoatMap.value=p.clearcoatMap,t(p.clearcoatMap,g.clearcoatMapTransform)),p.clearcoatRoughnessMap&&(g.clearcoatRoughnessMap.value=p.clearcoatRoughnessMap,t(p.clearcoatRoughnessMap,g.clearcoatRoughnessMapTransform)),p.clearcoatNormalMap&&(g.clearcoatNormalMap.value=p.clearcoatNormalMap,t(p.clearcoatNormalMap,g.clearcoatNormalMapTransform),g.clearcoatNormalScale.value.copy(p.clearcoatNormalScale),p.side===yn&&g.clearcoatNormalScale.value.negate())),p.dispersion>0&&(g.dispersion.value=p.dispersion),p.retroreflectivity>0&&(g.retroreflectivity.value=p.retroreflectivity),p.iridescence>0&&(g.iridescence.value=p.iridescence,g.iridescenceIOR.value=p.iridescenceIOR,g.iridescenceThicknessMinimum.value=p.iridescenceThicknessRange[0],g.iridescenceThicknessMaximum.value=p.iridescenceThicknessRange[1],p.iridescenceMap&&(g.iridescenceMap.value=p.iridescenceMap,t(p.iridescenceMap,g.iridescenceMapTransform)),p.iridescenceThicknessMap&&(g.iridescenceThicknessMap.value=p.iridescenceThicknessMap,t(p.iridescenceThicknessMap,g.iridescenceThicknessMapTransform))),p.transmission>0&&(g.transmission.value=p.transmission,g.transmissionSamplerMap.value=_.texture,g.transmissionSamplerSize.value.set(_.width,_.height),p.transmissionMap&&(g.transmissionMap.value=p.transmissionMap,t(p.transmissionMap,g.transmissionMapTransform)),g.thickness.value=p.thickness,p.thicknessMap&&(g.thicknessMap.value=p.thicknessMap,t(p.thicknessMap,g.thicknessMapTransform)),g.attenuationDistance.value=p.attenuationDistance,g.attenuationColor.value.copy(p.attenuationColor)),p.anisotropy>0&&(g.anisotropyVector.value.set(p.anisotropy*Math.cos(p.anisotropyRotation),p.anisotropy*Math.sin(p.anisotropyRotation)),p.anisotropyMap&&(g.anisotropyMap.value=p.anisotropyMap,t(p.anisotropyMap,g.anisotropyMapTransform))),g.specularIntensity.value=p.specularIntensity,g.specularColor.value.copy(p.specularColor),p.specularColorMap&&(g.specularColorMap.value=p.specularColorMap,t(p.specularColorMap,g.specularColorMapTransform)),p.specularIntensityMap&&(g.specularIntensityMap.value=p.specularIntensityMap,t(p.specularIntensityMap,g.specularIntensityMapTransform))}function m(g,p){p.matcap&&(g.matcap.value=p.matcap)}function b(g,p){let _=e.get(p).light;g.referencePosition.value.setFromMatrixPosition(_.matrixWorld),g.nearDistance.value=_.shadow.camera.near,g.farDistance.value=_.shadow.camera.far}return{refreshFogUniforms:n,refreshMaterialUniforms:s}}function fy(i,e,t,n){let s={},r={},a=[],o=i.getParameter(i.MAX_UNIFORM_BUFFER_BINDINGS);function c(x,M){let E=M.program;n.uniformBlockBinding(x,E)}function l(x,M){let E=s[x.id];E===void 0&&(g(x),E=h(x),s[x.id]=E,x.addEventListener("dispose",_));let A=M.program;n.updateUBOMapping(x,A);let y=e.render.frame;r[x.id]!==y&&(d(x),r[x.id]=y)}function h(x){let M=u();x.__bindingPointIndex=M;let E=i.createBuffer(),A=x.__size,y=x.usage;return i.bindBuffer(i.UNIFORM_BUFFER,E),i.bufferData(i.UNIFORM_BUFFER,A,y),i.bindBuffer(i.UNIFORM_BUFFER,null),i.bindBufferBase(i.UNIFORM_BUFFER,M,E),E}function u(){for(let x=0;x<o;x++)if(a.indexOf(x)===-1)return a.push(x),x;return Ye("WebGLRenderer: Maximum number of simultaneously usable uniforms groups reached."),0}function d(x){let M=s[x.id],E=x.uniforms,A=x.__cache;i.bindBuffer(i.UNIFORM_BUFFER,M);for(let y=0,w=E.length;y<w;y++){let R=E[y];if(Array.isArray(R))for(let I=0,L=R.length;I<L;I++)f(R[I],y,I,A);else f(R,y,0,A)}i.bindBuffer(i.UNIFORM_BUFFER,null)}function f(x,M,E,A){if(b(x,M,E,A)===!0){let y=x.__offset,w=x.value;if(Array.isArray(w)){let R=0;for(let I=0;I<w.length;I++){let L=w[I],F=p(L);m(L,x.__data,R),typeof L!="number"&&typeof L!="boolean"&&!L.isMatrix3&&!ArrayBuffer.isView(L)&&(R+=F.storage/Float32Array.BYTES_PER_ELEMENT)}}else m(w,x.__data,0);i.bufferSubData(i.UNIFORM_BUFFER,y,x.__data)}}function m(x,M,E){typeof x=="number"||typeof x=="boolean"?M[0]=x:x.isMatrix3?(M[0]=x.elements[0],M[1]=x.elements[1],M[2]=x.elements[2],M[3]=0,M[4]=x.elements[3],M[5]=x.elements[4],M[6]=x.elements[5],M[7]=0,M[8]=x.elements[6],M[9]=x.elements[7],M[10]=x.elements[8],M[11]=0):ArrayBuffer.isView(x)?M.set(new x.constructor(x.buffer,x.byteOffset,M.length)):x.toArray(M,E)}function b(x,M,E,A){let y=x.value,w=M+"_"+E;if(A[w]===void 0)return typeof y=="number"||typeof y=="boolean"?A[w]=y:ArrayBuffer.isView(y)?A[w]=y.slice():A[w]=y.clone(),!0;{let R=A[w];if(typeof y=="number"||typeof y=="boolean"){if(R!==y)return A[w]=y,!0}else{if(ArrayBuffer.isView(y))return!0;if(R.equals(y)===!1)return R.copy(y),!0}}return!1}function g(x){let M=x.uniforms,E=0,A=16;for(let w=0,R=M.length;w<R;w++){let I=Array.isArray(M[w])?M[w]:[M[w]];for(let L=0,F=I.length;L<F;L++){let D=I[L],k=Array.isArray(D.value)?D.value:[D.value];for(let W=0,X=k.length;W<X;W++){let re=k[W],j=p(re),ee=E%A,te=ee%j.boundary,Ne=ee+te;E+=te,Ne!==0&&A-Ne<j.storage&&(E+=A-Ne),D.__data=new Float32Array(j.storage/Float32Array.BYTES_PER_ELEMENT),D.__offset=E,E+=j.storage}}}let y=E%A;return y>0&&(E+=A-y),x.__size=E,x.__cache={},this}function p(x){let M={boundary:0,storage:0};return typeof x=="number"||typeof x=="boolean"?(M.boundary=4,M.storage=4):x.isVector2?(M.boundary=8,M.storage=8):x.isVector3||x.isColor?(M.boundary=16,M.storage=12):x.isVector4?(M.boundary=16,M.storage=16):x.isMatrix3?(M.boundary=48,M.storage=48):x.isMatrix4?(M.boundary=64,M.storage=64):x.isTexture?Ve("WebGLRenderer: Texture samplers can not be part of an uniforms group."):ArrayBuffer.isView(x)?(M.boundary=16,M.storage=x.byteLength):Ve("WebGLRenderer: Unsupported uniform value type.",x),M}function _(x){let M=x.target;M.removeEventListener("dispose",_);let E=a.indexOf(M.__bindingPointIndex);a.splice(E,1),i.deleteBuffer(s[M.id]),delete s[M.id],delete r[M.id]}function S(){for(let x in s)i.deleteBuffer(s[x]);a=[],s={},r={}}return{bind:c,update:l,dispose:S}}var py=new Uint16Array([12469,15057,12620,14925,13266,14620,13807,14376,14323,13990,14545,13625,14713,13328,14840,12882,14931,12528,14996,12233,15039,11829,15066,11525,15080,11295,15085,10976,15082,10705,15073,10495,13880,14564,13898,14542,13977,14430,14158,14124,14393,13732,14556,13410,14702,12996,14814,12596,14891,12291,14937,11834,14957,11489,14958,11194,14943,10803,14921,10506,14893,10278,14858,9960,14484,14039,14487,14025,14499,13941,14524,13740,14574,13468,14654,13106,14743,12678,14818,12344,14867,11893,14889,11509,14893,11180,14881,10751,14852,10428,14812,10128,14765,9754,14712,9466,14764,13480,14764,13475,14766,13440,14766,13347,14769,13070,14786,12713,14816,12387,14844,11957,14860,11549,14868,11215,14855,10751,14825,10403,14782,10044,14729,9651,14666,9352,14599,9029,14967,12835,14966,12831,14963,12804,14954,12723,14936,12564,14917,12347,14900,11958,14886,11569,14878,11247,14859,10765,14828,10401,14784,10011,14727,9600,14660,9289,14586,8893,14508,8533,15111,12234,15110,12234,15104,12216,15092,12156,15067,12010,15028,11776,14981,11500,14942,11205,14902,10752,14861,10393,14812,9991,14752,9570,14682,9252,14603,8808,14519,8445,14431,8145,15209,11449,15208,11451,15202,11451,15190,11438,15163,11384,15117,11274,15055,10979,14994,10648,14932,10343,14871,9936,14803,9532,14729,9218,14645,8742,14556,8381,14461,8020,14365,7603,15273,10603,15272,10607,15267,10619,15256,10631,15231,10614,15182,10535,15118,10389,15042,10167,14963,9787,14883,9447,14800,9115,14710,8665,14615,8318,14514,7911,14411,7507,14279,7198,15314,9675,15313,9683,15309,9712,15298,9759,15277,9797,15229,9773,15166,9668,15084,9487,14995,9274,14898,8910,14800,8539,14697,8234,14590,7790,14479,7409,14367,7067,14178,6621,15337,8619,15337,8631,15333,8677,15325,8769,15305,8871,15264,8940,15202,8909,15119,8775,15022,8565,14916,8328,14804,8009,14688,7614,14569,7287,14448,6888,14321,6483,14088,6171,15350,7402,15350,7419,15347,7480,15340,7613,15322,7804,15287,7973,15229,8057,15148,8012,15046,7846,14933,7611,14810,7357,14682,7069,14552,6656,14421,6316,14251,5948,14007,5528,15356,5942,15356,5977,15353,6119,15348,6294,15332,6551,15302,6824,15249,7044,15171,7122,15070,7050,14949,6861,14818,6611,14679,6349,14538,6067,14398,5651,14189,5311,13935,4958,15359,4123,15359,4153,15356,4296,15353,4646,15338,5160,15311,5508,15263,5829,15188,6042,15088,6094,14966,6001,14826,5796,14678,5543,14527,5287,14377,4985,14133,4586,13869,4257,15360,1563,15360,1642,15358,2076,15354,2636,15341,3350,15317,4019,15273,4429,15203,4732,15105,4911,14981,4932,14836,4818,14679,4621,14517,4386,14359,4156,14083,3795,13808,3437,15360,122,15360,137,15358,285,15355,636,15344,1274,15322,2177,15281,2765,15215,3223,15120,3451,14995,3569,14846,3567,14681,3466,14511,3305,14344,3121,14037,2800,13753,2467,15360,0,15360,1,15359,21,15355,89,15346,253,15325,479,15287,796,15225,1148,15133,1492,15008,1749,14856,1882,14685,1886,14506,1783,14324,1608,13996,1398,13702,1183]),Bi=null;function my(){return Bi===null&&(Bi=new Br(py,16,16,Ps,Si),Bi.name="DFG_LUT",Bi.minFilter=Xt,Bi.magFilter=Xt,Bi.wrapS=ni,Bi.wrapT=ni,Bi.generateMipmaps=!1,Bi.needsUpdate=!0),Bi}var Rl=class{constructor(e={}){let{canvas:t=pp(),context:n=null,depth:s=!0,stencil:r=!1,alpha:a=!1,antialias:o=!1,premultipliedAlpha:c=!0,preserveDrawingBuffer:l=!1,powerPreference:h="default",failIfMajorPerformanceCaveat:u=!1,reversedDepthBuffer:d=!1,outputBufferType:f=Un}=e;this.isWebGLRenderer=!0;let m;if(n!==null){if(typeof WebGLRenderingContext<"u"&&n instanceof WebGLRenderingContext)throw new Error("THREE.WebGLRenderer: WebGL 1 is not supported since r163.");m=n.getContextAttributes().alpha}else m=a;let b=f,g=new Set([Xc,qc,Wc]),p=new Set([Un,Mi,Yr,Jr,Hc,Gc]),_=new Uint32Array(4),S=new Int32Array(4),x=new P,M=null,E=null,A=[],y=[],w=null;this.domElement=t,this.debug={checkShaderErrors:!0,diagnostics:{keywords:!1},onShaderError:null},this.autoClear=!0,this.autoClearColor=!0,this.autoClearDepth=!0,this.autoClearStencil=!0,this.sortObjects=!0,this.clippingPlanes=[],this.localClippingEnabled=!1,this.toneMapping=vi,this.toneMappingExposure=1,this.transmissionResolutionScale=1;let R=this,I=!1,L=null,F=null,D=null,k=null;this._outputColorSpace=pt;let W=0,X=0,re=null,j=-1,ee=null,te=new Pt,Ne=new Pt,Se=null,ct=new Be(0),$e=0,ht=t.width,J=t.height,ie=1,ve=null,Ke=null,Ae=new Pt(0,0,ht,J),ze=new Pt(0,0,ht,J),bt=!1,Q=new kr,ce=!1,le=!1,de=new Ze,xe=new P,Xe=new Pt,Fe={background:null,fog:null,environment:null,overrideMaterial:null,isScene:!0},qe=!1;function Je(){return re===null?ie:1}let N=n;function xt(T,O){return t.getContext(T,O)}let st,C,v,z,V,Y,pe,me,Z,ne,ye,He,Me,q,oe,ge,je,U,fe,B,ue,be,se;try{let T={alpha:!0,depth:s,stencil:r,antialias:o,premultipliedAlpha:c,preserveDrawingBuffer:l,powerPreference:h,failIfMajorPerformanceCaveat:u};if("setAttribute"in t&&t.setAttribute("data-engine",`three.js r${"186"}`),t.addEventListener("webglcontextlost",kt,!1),t.addEventListener("webglcontextrestored",Et,!1),t.addEventListener("webglcontextcreationerror",fi,!1),N===null){let O="webgl2";if(N=xt(O,T),N===null)throw xt(O)?new Error("THREE.WebGLRenderer: Error creating WebGL context with your selected attributes."):new Error("THREE.WebGLRenderer: Error creating WebGL context.")}Ie()}catch(T){throw t.removeEventListener("webglcontextlost",kt,!1),t.removeEventListener("webglcontextrestored",Et,!1),t.removeEventListener("webglcontextcreationerror",fi,!1),Ye("WebGLRenderer: "+T.message),T}function Ie(){st=new M_(N),st.init(),ue=new cy(N,st),C=new d_(N,st,e,ue),v=new ay(N,st),C.reversedDepthBuffer&&d&&v.buffers.depth.setReversed(!0),F=N.createFramebuffer(),D=N.createFramebuffer(),k=N.createFramebuffer(),z=new T_(N),V=new X1,Y=new oy(N,st,v,V,C,ue,z),pe=new v_(R),me=new Ag(N),be=new h_(N,me),Z=new S_(N,me,z,be),ne=new A_(N,Z,me,be,z),U=new w_(N,C,Y),oe=new f_(V),ye=new q1(R,pe,st,C,be,oe),He=new dy(R,V),Me=new K1,q=new ey(st),je=new l_(R,pe,v,ne,m,c),ge=new ry(R,ne,C),se=new fy(N,z,C,v),fe=new u_(N,st,z),B=new E_(N,st,z),z.programs=ye.programs,R.capabilities=C,R.extensions=st,R.properties=V,R.renderLists=Me,R.shadowMap=ge,R.state=v,R.info=z}b!==Un&&(w=new C_(b,t.width,t.height,o,s,r));let Pe=new Du(R,N);this.xr=Pe,this.getContext=function(){return N},this.getContextAttributes=function(){return N.getContextAttributes()},this.forceContextLoss=function(){let T=st.get("WEBGL_lose_context");T&&T.loseContext()},this.forceContextRestore=function(){let T=st.get("WEBGL_lose_context");T&&T.restoreContext()},this.getPixelRatio=function(){return ie},this.setPixelRatio=function(T){T!==void 0&&(ie=T,this.setSize(ht,J,!1))},this.getSize=function(T){return T.set(ht,J)},this.setSize=function(T,O,K=!0){if(Pe.isPresenting){Ve("WebGLRenderer: Can't change size while VR device is presenting.");return}ht=T,J=O,t.width=Math.floor(T*ie),t.height=Math.floor(O*ie),K===!0&&(t.style.width=T+"px",t.style.height=O+"px"),w!==null&&w.setSize(t.width,t.height),this.setViewport(0,0,T,O)},this.getDrawingBufferSize=function(T){return T.set(ht*ie,J*ie).floor()},this.setDrawingBufferSize=function(T,O,K){ht=T,J=O,ie=K,t.width=Math.floor(T*K),t.height=Math.floor(O*K),this.setViewport(0,0,T,O)},this.setEffects=function(T){if(b===Un){Ye("WebGLRenderer: setEffects() requires outputBufferType set to HalfFloatType or FloatType.");return}if(T){for(let O=0;O<T.length;O++)if(T[O].isOutputPass===!0){Ve("WebGLRenderer: OutputPass is not needed in setEffects(). Tone mapping and color space conversion are applied automatically.");break}}w.setEffects(T||[])},this.getCurrentViewport=function(T){return T.copy(te)},this.getViewport=function(T){return T.copy(Ae)},this.setViewport=function(T,O,K,H){T.isVector4?Ae.set(T.x,T.y,T.z,T.w):Ae.set(T,O,K,H),v.viewport(te.copy(Ae).multiplyScalar(ie).round())},this.getScissor=function(T){return T.copy(ze)},this.setScissor=function(T,O,K,H){T.isVector4?ze.set(T.x,T.y,T.z,T.w):ze.set(T,O,K,H),v.scissor(Ne.copy(ze).multiplyScalar(ie).round())},this.getScissorTest=function(){return bt},this.setScissorTest=function(T){v.setScissorTest(bt=T)},this.setOpaqueSort=function(T){ve=T},this.setTransparentSort=function(T){Ke=T},this.getClearColor=function(T){return T.copy(je.getClearColor())},this.setClearColor=function(){je.setClearColor(...arguments)},this.getClearAlpha=function(){return je.getClearAlpha()},this.setClearAlpha=function(){je.setClearAlpha(...arguments)},this.clear=function(T=!0,O=!0,K=!0){let H=0;if(T){let G=!1;if(re!==null){let we=re.texture.format;G=g.has(we)}if(G){let we=re.texture.type,De=p.has(we),Te=je.getClearColor(),Ue=je.getClearAlpha(),Ge=Te.r,rt=Te.g,dt=Te.b;De?(_[0]=Ge,_[1]=rt,_[2]=dt,_[3]=Ue,N.clearBufferuiv(N.COLOR,0,_)):(S[0]=Ge,S[1]=rt,S[2]=dt,S[3]=Ue,N.clearBufferiv(N.COLOR,0,S))}else H|=N.COLOR_BUFFER_BIT}O&&(H|=N.DEPTH_BUFFER_BIT,this.state.buffers.depth.setMask(!0)),K&&(H|=N.STENCIL_BUFFER_BIT,this.state.buffers.stencil.setMask(4294967295)),H!==0&&N.clear(H)},this.clearColor=function(){this.clear(!0,!1,!1)},this.clearDepth=function(){this.clear(!1,!0,!1)},this.clearStencil=function(){this.clear(!1,!1,!0)},this.setNodesHandler=function(T){T.setRenderer(this),L=T},this.dispose=function(){t.removeEventListener("webglcontextlost",kt,!1),t.removeEventListener("webglcontextrestored",Et,!1),t.removeEventListener("webglcontextcreationerror",fi,!1),je.dispose(),Me.dispose(),q.dispose(),V.dispose(),pe.dispose(),ne.dispose(),be.dispose(),se.dispose(),ye.dispose(),Pe.dispose(),Pe.removeEventListener("sessionstart",Nd),Pe.removeEventListener("sessionend",Fd),Os.stop()};function kt(T){T.preventDefault(),Ta("WebGLRenderer: Context Lost."),I=!0}function Et(){Ta("WebGLRenderer: Context Restored."),I=!1;let T=z.autoReset,O=ge.enabled,K=ge.autoUpdate,H=ge.needsUpdate,G=ge.type;Ie(),z.autoReset=T,ge.enabled=O,ge.autoUpdate=K,ge.needsUpdate=H,ge.type=G}function fi(T){Ye("WebGLRenderer: A WebGL context could not be created. Reason: ",T.statusMessage)}function Ti(T){let O=T.target;O.removeEventListener("dispose",Ti),V0(O)}function V0(T){W0(T),V.remove(T)}function W0(T){let O=V.get(T).programs;O!==void 0&&(O.forEach(function(K){ye.releaseProgram(K)}),T.isShaderMaterial&&ye.releaseShaderCache(T))}this.renderBufferDirect=function(T,O,K,H,G,we){O===null&&(O=Fe);let De=G.isMesh&&G.matrixWorld.determinantAffine()<0,Te=j0(T,O,K,H,G);v.setMaterial(H,De);let Ue=K.index,Ge=1;if(H.wireframe===!0){if(Ue=Z.getWireframeAttribute(K),Ue===void 0)return;Ge=2}let rt=K.drawRange,dt=K.attributes.position,Oe=rt.start*Ge,Tt=(rt.start+rt.count)*Ge;we!==null&&(Oe=Math.max(Oe,we.start*Ge),Tt=Math.min(Tt,(we.start+we.count)*Ge)),Ue!==null?(Oe=Math.max(Oe,0),Tt=Math.min(Tt,Ue.count)):dt!=null&&(Oe=Math.max(Oe,0),Tt=Math.min(Tt,dt.count));let sn=Tt-Oe;if(sn<0||sn===1/0)return;be.setup(G,H,Te,K,Ue);let Gt,Ut=fe;if(Ue!==null&&(Gt=me.get(Ue),Ut=B,Ut.setIndex(Gt)),G.isMesh)H.wireframe===!0?(v.setLineWidth(H.wireframeLinewidth*Je()),Ut.setMode(N.LINES)):Ut.setMode(N.TRIANGLES);else if(G.isLine){let Sn=H.linewidth;Sn===void 0&&(Sn=1),v.setLineWidth(Sn*Je()),G.isLineSegments?Ut.setMode(N.LINES):G.isLineLoop?Ut.setMode(N.LINE_LOOP):Ut.setMode(N.LINE_STRIP)}else G.isPoints?Ut.setMode(N.POINTS):G.isSprite&&Ut.setMode(N.TRIANGLES);if(G.isBatchedMesh)if(st.get("WEBGL_multi_draw"))Ut.renderMultiDraw(G._multiDrawStarts,G._multiDrawCounts,G._multiDrawCount);else{let Sn=G._multiDrawStarts,Le=G._multiDrawCounts,In=G._multiDrawCount,_t=Ue?me.get(Ue).bytesPerElement:1,ei=V.get(H).currentProgram.getUniforms();for(let wi=0;wi<In;wi++)ei.setValue(N,"_gl_DrawID",wi),Ut.render(Sn[wi]/_t,Le[wi])}else if(G.isInstancedMesh)Ut.renderInstances(Oe,sn,G.count);else if(K.isInstancedBufferGeometry){let Sn=K._maxInstanceCount!==void 0?K._maxInstanceCount:1/0,Le=Math.min(K.instanceCount,Sn);Ut.renderInstances(Oe,sn,Le)}else Ut.render(Oe,sn)};function Dd(T,O,K,H){L!==null&&T.isNodeMaterial&&L.setObject(H,T),ce===!0&&oe.setState(T,K,!1),T.transparent===!0&&T.side===oi&&T.forceSinglePass===!1?(T.side=yn,T.needsUpdate=!0,Ro(T,O,H),T.side=Ui,T.needsUpdate=!0,Ro(T,O,H),T.side=oi):Ro(T,O,H)}this.compile=function(T,O,K=null){K===null&&(K=T),L!==null&&L.renderStart(T,O,K),E=q.get(K),E.init(O),y.push(E),K.traverseVisible(function(G){G.isLight&&G.layers.test(O.layers)&&(E.pushLight(G),G.castShadow&&E.pushShadow(G))}),T!==K&&T.traverseVisible(function(G){G.isLight&&G.layers.test(O.layers)&&(E.pushLight(G),G.castShadow&&E.pushShadow(G))}),E.setupLights(),L!==null&&L.updateLights(E.state.lightsArray),le=this.localClippingEnabled,ce=oe.init(this.clippingPlanes,le),ce===!0&&oe.setGlobalState(this.clippingPlanes,O),L!==null&&ge.render(E.state.shadowsArray,K,O);let H=new Set;return T.traverse(function(G){if(!(G.isMesh||G.isPoints||G.isLine||G.isSprite))return;let we=G.material;if(we)if(Array.isArray(we))for(let De=0;De<we.length;De++){let Te=we[De];Dd(Te,K,O,G),H.add(Te)}else Dd(we,K,O,G),H.add(we)}),E=y.pop(),L!==null&&L.renderEnd(),H},this.compileAsync=function(T,O,K=null){let H=this.compile(T,O,K);return new Promise(G=>{function we(){if(H.forEach(function(De){let Ue=V.get(De).currentProgram;(Ue===void 0||Ue.isReady())&&H.delete(De)}),H.size===0){G(T);return}setTimeout(we,10)}st.get("KHR_parallel_shader_compile")!==null?we():setTimeout(we,10)})};let eh=null;function q0(T){eh&&eh(T)}function Nd(){Os.stop()}function Fd(){Os.start()}let Os=new jp;Os.setAnimationLoop(q0),typeof self<"u"&&Os.setContext(self),this.setAnimationLoop=function(T){eh=T,Pe.setAnimationLoop(T),T===null?Os.stop():Os.start()},Pe.addEventListener("sessionstart",Nd),Pe.addEventListener("sessionend",Fd),this.render=function(T,O){if(O!==void 0&&O.isCamera!==!0){Ye("WebGLRenderer.render: camera is not an instance of THREE.Camera.");return}if(I===!0)return;L!==null&&L.renderStart(T,O);let K=Pe.enabled===!0&&Pe.isPresenting===!0,H=w!==null&&(re===null||K)&&w.begin(R,re);if(T.matrixWorldAutoUpdate===!0&&T.updateMatrixWorld(),O.parent===null&&O.matrixWorldAutoUpdate===!0&&O.updateMatrixWorld(),Pe.enabled===!0&&Pe.isPresenting===!0&&(w===null||w.isCompositing()===!1)&&(Pe.cameraAutoUpdate===!0&&Pe.updateCamera(O),O=Pe.getCamera()),T.isScene===!0&&T.onBeforeRender(R,T,O,re),E=q.get(T,y.length),E.init(O),E.state.textureUnits=Y.getTextureUnits(),y.push(E),de.multiplyMatrices(O.projectionMatrix,O.matrixWorldInverse),Q.setFromProjectionMatrix(de,xi,O.reversedDepth),le=this.localClippingEnabled,ce=oe.init(this.clippingPlanes,le),M=Me.get(T,A.length),M.init(),A.push(M),Pe.enabled===!0&&Pe.isPresenting===!0){let De=R.xr.getDepthSensingMesh();De!==null&&th(De,O,-1/0,R.sortObjects)}th(T,O,0,R.sortObjects),M.finish(),L!==null&&L.updateLights(E.state.lightsArray),R.sortObjects===!0&&M.sort(ve,Ke),qe=Pe.enabled===!1||Pe.isPresenting===!1||Pe.hasDepthSensing()===!1,qe&&je.addToRenderList(M,T),this.info.render.frame++,this.info.autoReset===!0&&this.info.reset(),ce===!0&&oe.beginShadows();let G=E.state.shadowsArray;if(ge.render(G,T,O),ce===!0&&oe.endShadows(),(H&&w.hasRenderPass())===!1){let De=M.opaque,Te=M.transmissive;if(E.setupLights(),O.isArrayCamera){let Ue=O.cameras;if(Te.length>0)for(let Ge=0,rt=Ue.length;Ge<rt;Ge++){let dt=Ue[Ge];Od(De,Te,T,dt)}qe&&je.render(T);for(let Ge=0,rt=Ue.length;Ge<rt;Ge++){let dt=Ue[Ge];Ud(M,T,dt,dt.viewport)}}else Te.length>0&&Od(De,Te,T,O),qe&&je.render(T),Ud(M,T,O)}re!==null&&X===0&&(Y.updateMultisampleRenderTarget(re),Y.updateRenderTargetMipmap(re)),H&&w.end(R),T.isScene===!0&&T.onAfterRender(R,T,O),be.resetDefaultState(),j=-1,ee=null,y.pop(),y.length>0?(E=y[y.length-1],Y.setTextureUnits(E.state.textureUnits),ce===!0&&oe.setGlobalState(R.clippingPlanes,E.state.camera)):E=null,A.pop(),A.length>0?M=A[A.length-1]:M=null,L!==null&&L.renderEnd()};function th(T,O,K,H){if(T.visible===!1)return;if(T.layers.test(O.layers)){if(T.isGroup)K=T.renderOrder;else if(T.isLOD)T.autoUpdate===!0&&T.update(O);else if(T.isLightProbeGrid)E.pushLightProbeGrid(T);else if(T.isLight)E.pushLight(T),T.castShadow&&E.pushShadow(T);else if(T.isSprite){if(!T.frustumCulled||T.intersectsFrustum(Q)){H&&Xe.setFromMatrixPosition(T.matrixWorld).applyMatrix4(de);let De=ne.update(T),Te=T.material;Te.visible&&M.push(T,De,Te,K,Xe.z,null,O)}}else if((T.isMesh||T.isLine||T.isPoints)&&(!T.frustumCulled||T.intersectsFrustum(Q))){let De=ne.update(T),Te=T.material;if(H&&(T.boundingSphere!==void 0?(T.boundingSphere===null&&T.computeBoundingSphere(),Xe.copy(T.boundingSphere.center)):(De.boundingSphere===null&&De.computeBoundingSphere(),Xe.copy(De.boundingSphere.center)),Xe.applyMatrix4(T.matrixWorld).applyMatrix4(de)),Array.isArray(Te)){let Ue=De.groups;for(let Ge=0,rt=Ue.length;Ge<rt;Ge++){let dt=Ue[Ge],Oe=Te[dt.materialIndex];Oe&&Oe.visible&&M.push(T,De,Oe,K,Xe.z,dt,O)}}else Te.visible&&M.push(T,De,Te,K,Xe.z,null,O)}}let we=T.children;for(let De=0,Te=we.length;De<Te;De++)th(we[De],O,K,H)}function Ud(T,O,K,H){let{opaque:G,transmissive:we,transparent:De}=T;E.setupLightsView(K),ce===!0&&oe.setGlobalState(R.clippingPlanes,K),H&&v.viewport(te.copy(H)),G.length>0&&Ao(G,O,K),we.length>0&&Ao(we,O,K),De.length>0&&Ao(De,O,K),v.buffers.depth.setTest(!0),v.buffers.depth.setMask(!0),v.buffers.color.setMask(!0),v.setPolygonOffset(!1)}function Od(T,O,K,H){if((K.isScene===!0?K.overrideMaterial:null)!==null)return;if(E.state.transmissionRenderTarget[H.id]===void 0){let Oe=st.has("EXT_color_buffer_half_float")||st.has("EXT_color_buffer_float");E.state.transmissionRenderTarget[H.id]=new wn(1,1,{generateMipmaps:!0,type:Oe?Si:Un,minFilter:qn,samples:Math.max(4,C.samples),stencilBuffer:r,resolveDepthBuffer:!1,resolveStencilBuffer:!1,storeMultisampledDepthBuffer:!1,storeMultisampledStencilBuffer:!1,colorSpace:lt.workingColorSpace})}let we=E.state.transmissionRenderTarget[H.id],De=H.viewport||te;we.setSize(De.z*R.transmissionResolutionScale,De.w*R.transmissionResolutionScale);let Te=R.getRenderTarget(),Ue=R.getActiveCubeFace(),Ge=R.getActiveMipmapLevel();R.setRenderTarget(we),R.getClearColor(ct),$e=R.getClearAlpha(),$e<1&&R.setClearColor(16777215,.5),R.clear(),qe&&je.render(K);let rt=R.toneMapping;R.toneMapping=vi;let dt=H.viewport;if(H.viewport!==void 0&&(H.viewport=void 0),E.setupLightsView(H),ce===!0&&oe.setGlobalState(R.clippingPlanes,H),Ao(T,K,H),Y.updateMultisampleRenderTarget(we),Y.updateRenderTargetMipmap(we),st.has("WEBGL_multisampled_render_to_texture")===!1){let Oe=!1;for(let Tt=0,sn=O.length;Tt<sn;Tt++){let Gt=O[Tt],{object:Ut,geometry:Sn,material:Le,group:In}=Gt;if(Le.side===oi&&Ut.layers.test(H.layers)){let _t=Le.side;Le.side=yn,Le.needsUpdate=!0,Bd(Ut,K,H,Sn,Le,In),Le.side=_t,Le.needsUpdate=!0,Oe=!0}}Oe===!0&&(Y.updateMultisampleRenderTarget(we),Y.updateRenderTargetMipmap(we))}R.setRenderTarget(Te,Ue,Ge),R.setClearColor(ct,$e),dt!==void 0&&(H.viewport=dt),R.toneMapping=rt}function Ao(T,O,K){let H=O.isScene===!0?O.overrideMaterial:null;for(let G=0,we=T.length;G<we;G++){let De=T[G],{object:Te,geometry:Ue,group:Ge}=De,rt=De.material;rt.allowOverride===!0&&H!==null&&(rt=H),Te.layers.test(K.layers)&&Bd(Te,O,K,Ue,rt,Ge)}}function Bd(T,O,K,H,G,we){L!==null&&G.isNodeMaterial&&L.setObject(T,G),T.onBeforeRender(R,O,K,H,G,we),T.modelViewMatrix.multiplyMatrices(K.matrixWorldInverse,T.matrixWorld),T.normalMatrix.getNormalMatrix(T.modelViewMatrix),G.onBeforeRender(R,O,K,H,T,we),G.transparent===!0&&G.side===oi&&G.forceSinglePass===!1?(G.side=yn,G.needsUpdate=!0,R.renderBufferDirect(K,O,H,G,T,we),G.side=Ui,G.needsUpdate=!0,R.renderBufferDirect(K,O,H,G,T,we),G.side=oi):R.renderBufferDirect(K,O,H,G,T,we),T.onAfterRender(R,O,K,H,G,we)}function Ro(T,O,K){O.isScene!==!0&&(O=Fe);let H=V.get(T),G=E.state.lights,we=E.state.shadowsArray,De=G.state.version,Te=ye.getParameters(T,G.state,we,O,K,E.state.lightProbeGridArray),Ue=ye.getProgramCacheKey(Te),Ge=H.programs;H.environment=T.isMeshStandardMaterial||T.isMeshLambertMaterial||T.isMeshPhongMaterial?O.environment:null,H.fog=O.fog;let rt=T.isMeshStandardMaterial||T.isMeshLambertMaterial&&!T.envMap||T.isMeshPhongMaterial&&!T.envMap;H.envMap=pe.get(T.envMap||H.environment,rt),H.envMapRotation=H.environment!==null&&T.envMap===null?O.environmentRotation:T.envMapRotation,Ge===void 0&&(T.addEventListener("dispose",Ti),Ge=new Map,H.programs=Ge);let dt=Ge.get(Ue);if(dt!==void 0){if(H.currentProgram===dt&&H.lightsStateVersion===De)return zd(T,Te),dt}else Te.uniforms=ye.getUniforms(T),L!==null&&T.isNodeMaterial&&L.build(T,K,Te),T.onBeforeCompile(Te,R),dt=ye.acquireProgram(Te,Ue),Ge.set(Ue,dt),H.uniforms=Te.uniforms;let Oe=H.uniforms;return(!T.isShaderMaterial&&!T.isRawShaderMaterial||T.clipping===!0)&&(Oe.clippingPlanes=oe.uniform),zd(T,Te),H.needsLights=Y0(T),H.lightsStateVersion=De,H.needsLights&&(Oe.ambientLightColor.value=G.state.ambient,Oe.lightProbe.value=G.state.probe,Oe.sunLights.value=G.state.sun,Oe.sunLightShadows.value=G.state.sunShadow,Oe.directionalLights.value=G.state.directional,Oe.directionalLightShadows.value=G.state.directionalShadow,Oe.spotLights.value=G.state.spot,Oe.spotLightShadows.value=G.state.spotShadow,Oe.rectAreaLights.value=G.state.rectArea,Oe.ltc_1.value=G.state.rectAreaLTC1,Oe.ltc_2.value=G.state.rectAreaLTC2,Oe.pointLights.value=G.state.point,Oe.pointLightShadows.value=G.state.pointShadow,Oe.hemisphereLights.value=G.state.hemi,Oe.sunShadowMatrix.value=G.state.sunShadowMatrix,Oe.sunShadowCascade.value=G.state.sunShadowCascade,Oe.directionalShadowMatrix.value=G.state.directionalShadowMatrix,Oe.spotLightMatrix.value=G.state.spotLightMatrix,Oe.spotLightMap.value=G.state.spotLightMap,Oe.pointShadowMatrix.value=G.state.pointShadowMatrix),H.lightProbeGrid=E.state.lightProbeGridArray.length>0,H.currentProgram=dt,H.uniformsList=null,dt}function kd(T){if(T.uniformsList===null){let O=T.currentProgram.getUniforms();T.uniformsList=ea.seqWithValue(O.seq,T.uniforms)}return T.uniformsList}function zd(T,O){let K=V.get(T);K.outputColorSpace=O.outputColorSpace,K.batching=O.batching,K.batchingColor=O.batchingColor,K.instancing=O.instancing,K.instancingColor=O.instancingColor,K.instancingMorph=O.instancingMorph,K.skinning=O.skinning,K.morphTargets=O.morphTargets,K.morphNormals=O.morphNormals,K.morphColors=O.morphColors,K.morphTargetsCount=O.morphTargetsCount,K.numClippingPlanes=O.numClippingPlanes,K.numIntersection=O.numClipIntersection,K.vertexAlphas=O.vertexAlphas,K.vertexTangents=O.vertexTangents,K.toneMapping=O.toneMapping}function X0(T,O){if(T.length===0)return null;if(T.length===1)return T[0].texture!==null?T[0]:null;x.setFromMatrixPosition(O.matrixWorld);for(let K=0,H=T.length;K<H;K++){let G=T[K];if(G.texture!==null&&G.boundingBox.containsPoint(x))return G}return null}function j0(T,O,K,H,G){O.isScene!==!0&&(O=Fe),Y.resetTextureUnits();let we=O.fog,De=H.isMeshStandardMaterial||H.isMeshLambertMaterial||H.isMeshPhongMaterial?O.environment:null,Te=re===null?R.outputColorSpace:re.isXRRenderTarget===!0?re.texture.colorSpace:lt.workingColorSpace,Ue=H.isMeshStandardMaterial||H.isMeshLambertMaterial&&!H.envMap||H.isMeshPhongMaterial&&!H.envMap,Ge=pe.get(H.envMap||De,Ue),rt=H.vertexColors===!0&&!!K.attributes.color&&K.attributes.color.itemSize===4,dt=!!K.attributes.tangent&&(!!H.normalMap||H.anisotropy>0),Oe=!!K.morphAttributes.position,Tt=!!K.morphAttributes.normal,sn=!!K.morphAttributes.color,Gt=vi;H.toneMapped&&(re===null||re.isXRRenderTarget===!0)&&(Gt=R.toneMapping);let Ut=K.morphAttributes.position||K.morphAttributes.normal||K.morphAttributes.color,Sn=Ut!==void 0?Ut.length:0,Le=V.get(H),In=E.state.lights;if(ce===!0&&(le===!0||T!==ee)){let zt=T===ee&&H.id===j;oe.setState(H,T,zt)}let _t=!1;H.version===Le.__version?(Le.needsLights&&Le.lightsStateVersion!==In.state.version||Le.outputColorSpace!==Te||G.isBatchedMesh&&Le.batching===!1||!G.isBatchedMesh&&Le.batching===!0||G.isBatchedMesh&&Le.batchingColor===!0&&G._colorsTexture===null||G.isBatchedMesh&&Le.batchingColor===!1&&G._colorsTexture!==null||G.isInstancedMesh&&Le.instancing===!1||!G.isInstancedMesh&&Le.instancing===!0||G.isSkinnedMesh&&Le.skinning===!1||!G.isSkinnedMesh&&Le.skinning===!0||G.isInstancedMesh&&Le.instancingColor===!0&&G.instanceColor===null||G.isInstancedMesh&&Le.instancingColor===!1&&G.instanceColor!==null||G.isInstancedMesh&&Le.instancingMorph===!0&&G.morphTexture===null||G.isInstancedMesh&&Le.instancingMorph===!1&&G.morphTexture!==null||Le.envMap!==Ge||H.fog===!0&&Le.fog!==we||Le.numClippingPlanes!==void 0&&(Le.numClippingPlanes!==oe.numPlanes||Le.numIntersection!==oe.numIntersection)||Le.vertexAlphas!==rt||Le.vertexTangents!==dt||Le.morphTargets!==Oe||Le.morphNormals!==Tt||Le.morphColors!==sn||Le.toneMapping!==Gt||Le.morphTargetsCount!==Sn||!!Le.lightProbeGrid!=E.state.lightProbeGridArray.length>0)&&(_t=!0):(_t=!0,Le.__version=H.version);let ei=Le.currentProgram;_t===!0&&(ei=Ro(H,O,G),L&&H.isNodeMaterial&&L.onUpdateProgram(H,ei,Le));let wi=!1,ls=!1,hr=!1,Dt=ei.getUniforms(),Qt=Le.uniforms;if(v.useProgram(ei.program)&&(wi=!0,ls=!0,hr=!0),H.id!==j&&(j=H.id,ls=!0),Le.needsLights){let zt=X0(E.state.lightProbeGridArray,G);Le.lightProbeGrid!==zt&&(Le.lightProbeGrid=zt,ls=!0)}if(wi||ee!==T){v.buffers.depth.getReversed()&&T.reversedDepth!==!0&&(T._reversedDepth=!0,T.updateProjectionMatrix()),Dt.setValue(N,"projectionMatrix",T.projectionMatrix),Dt.setValue(N,"viewMatrix",T.matrixWorldInverse);let us=Dt.map.cameraPosition;us!==void 0&&us.setValue(N,xe.setFromMatrixPosition(T.matrixWorld)),C.logarithmicDepthBuffer&&Dt.setValue(N,"logDepthBufFC",2/(Math.log(T.far+1)/Math.LN2)),(H.isMeshPhongMaterial||H.isMeshToonMaterial||H.isMeshLambertMaterial||H.isMeshBasicMaterial||H.isMeshStandardMaterial||H.isShaderMaterial)&&Dt.setValue(N,"isOrthographic",T.isOrthographicCamera===!0),ee!==T&&(ee=T,ls=!0,hr=!0)}if(Le.needsLights&&(In.state.sunShadowMap.length>0&&Dt.setValue(N,"sunShadowMap",In.state.sunShadowMap,Y),In.state.directionalShadowMap.length>0&&Dt.setValue(N,"directionalShadowMap",In.state.directionalShadowMap,Y),In.state.spotShadowMap.length>0&&Dt.setValue(N,"spotShadowMap",In.state.spotShadowMap,Y),In.state.pointShadowMap.length>0&&Dt.setValue(N,"pointShadowMap",In.state.pointShadowMap,Y)),G.isSkinnedMesh){Dt.setOptional(N,G,"bindMatrix"),Dt.setOptional(N,G,"bindMatrixInverse");let zt=G.skeleton;zt&&(zt.boneTexture===null&&zt.computeBoneTexture(),Dt.setValue(N,"boneTexture",zt.boneTexture,Y))}G.isBatchedMesh&&(Dt.setOptional(N,G,"batchingTexture"),Dt.setValue(N,"batchingTexture",G._matricesTexture,Y),Dt.setOptional(N,G,"batchingIdTexture"),Dt.setValue(N,"batchingIdTexture",G._indirectTexture,Y),Dt.setOptional(N,G,"batchingColorTexture"),G._colorsTexture!==null&&Dt.setValue(N,"batchingColorTexture",G._colorsTexture,Y));let hs=K.morphAttributes;if((hs.position!==void 0||hs.normal!==void 0||hs.color!==void 0)&&U.update(G,K,ei),(ls||Le.receiveShadow!==G.receiveShadow)&&(Le.receiveShadow=G.receiveShadow,Dt.setValue(N,"receiveShadow",G.receiveShadow)),(H.isMeshStandardMaterial||H.isMeshLambertMaterial||H.isMeshPhongMaterial)&&H.envMap===null&&O.environment!==null&&(Qt.envMapIntensity.value=O.environmentIntensity),Qt.dfgLUT!==void 0&&(Qt.dfgLUT.value=my()),ls){if(Dt.setValue(N,"toneMappingExposure",R.toneMappingExposure),Le.needsLights&&K0(Qt,hr),we&&H.fog===!0&&He.refreshFogUniforms(Qt,we),He.refreshMaterialUniforms(Qt,H,ie,J,E.state.transmissionRenderTarget[T.id]),Le.needsLights&&Le.lightProbeGrid){let zt=Le.lightProbeGrid;Qt.probesSH.value=zt.texture,Qt.probesMin.value.copy(zt.boundingBox.min),Qt.probesMax.value.copy(zt.boundingBox.max),Qt.probesResolution.value.copy(zt.resolution)}ea.upload(N,kd(Le),Qt,Y)}if(H.isShaderMaterial&&H.uniformsNeedUpdate===!0&&(ea.upload(N,kd(Le),Qt,Y),H.uniformsNeedUpdate=!1),H.isSpriteMaterial&&Dt.setValue(N,"center",G.center),Dt.setValue(N,"modelViewMatrix",G.modelViewMatrix),Dt.setValue(N,"normalMatrix",G.normalMatrix),Dt.setValue(N,"modelMatrix",G.matrixWorld),H.uniformsGroups!==void 0){let zt=H.uniformsGroups;for(let us=0,ur=zt.length;us<ur;us++){let Gd=zt[us];se.update(Gd,ei),se.bind(Gd,ei)}}return ei}function K0(T,O){T.ambientLightColor.needsUpdate=O,T.lightProbe.needsUpdate=O,T.sunLights.needsUpdate=O,T.sunLightShadows.needsUpdate=O,T.directionalLights.needsUpdate=O,T.directionalLightShadows.needsUpdate=O,T.pointLights.needsUpdate=O,T.pointLightShadows.needsUpdate=O,T.spotLights.needsUpdate=O,T.spotLightShadows.needsUpdate=O,T.rectAreaLights.needsUpdate=O,T.hemisphereLights.needsUpdate=O}function Y0(T){return T.isMeshLambertMaterial||T.isMeshToonMaterial||T.isMeshPhongMaterial||T.isMeshStandardMaterial||T.isShadowMaterial||T.isShaderMaterial&&T.lights===!0}this.getActiveCubeFace=function(){return W},this.getActiveMipmapLevel=function(){return X},this.getRenderTarget=function(){return re},this.setRenderTargetTextures=function(T,O,K){let H=V.get(T);H.__autoAllocateDepthBuffer=T.resolveDepthBuffer===!1,H.__autoAllocateDepthBuffer===!1&&(H.__useRenderToTexture=!1),V.get(T.texture).__webglTexture=O,V.get(T.depthTexture).__webglTexture=H.__autoAllocateDepthBuffer?void 0:K,H.__hasExternalTextures=!0},this.setRenderTargetFramebuffer=function(T,O){let K=V.get(T);K.__webglFramebuffer=O,K.__useDefaultFramebuffer=O===void 0},this.setRenderTarget=function(T,O=0,K=0){re=T,W=O,X=K;let H=null,G=!1,we=!1;if(T){let Te=V.get(T);if(Te.__useDefaultFramebuffer!==void 0){v.bindFramebuffer(N.FRAMEBUFFER,Te.__webglFramebuffer),te.copy(T.viewport),Ne.copy(T.scissor),Se=T.scissorTest,v.viewport(te),v.scissor(Ne),v.setScissorTest(Se),j=-1;return}else if(Te.__webglFramebuffer===void 0)Y.setupRenderTarget(T);else if(Te.__hasExternalTextures)Y.rebindTextures(T,V.get(T.texture).__webglTexture,V.get(T.depthTexture).__webglTexture);else if(T.depthBuffer){let rt=T.depthTexture;if(Te.__boundDepthTexture!==rt){if(rt!==null&&V.has(rt)&&(T.width!==rt.image.width||T.height!==rt.image.height))throw new Error("THREE.WebGLRenderer: Attached DepthTexture is initialized to the incorrect size.");Y.setupDepthRenderbuffer(T)}}let Ue=T.texture;(Ue.isData3DTexture||Ue.isDataArrayTexture||Ue.isCompressedArrayTexture)&&(we=!0);let Ge=V.get(T).__webglFramebuffer;T.isWebGLCubeRenderTarget?(Array.isArray(Ge[O])?H=Ge[O][K]:H=Ge[O],G=!0):T.samples>0&&Y.useMultisampledRTT(T)===!1?H=V.get(T).__webglMultisampledFramebuffer:Array.isArray(Ge)?H=Ge[K]:H=Ge,te.copy(T.viewport),Ne.copy(T.scissor),Se=T.scissorTest}else te.copy(Ae).multiplyScalar(ie).floor(),Ne.copy(ze).multiplyScalar(ie).floor(),Se=bt;if(K!==0&&(H=F),v.bindFramebuffer(N.FRAMEBUFFER,H)&&v.drawBuffers(T,H),v.viewport(te),v.scissor(Ne),v.setScissorTest(Se),G){let Te=V.get(T.texture);N.framebufferTexture2D(N.FRAMEBUFFER,N.COLOR_ATTACHMENT0,N.TEXTURE_CUBE_MAP_POSITIVE_X+O,Te.__webglTexture,K)}else if(we){let Te=O;for(let Ue=0;Ue<T.textures.length;Ue++){let Ge=V.get(T.textures[Ue]);N.framebufferTextureLayer(N.FRAMEBUFFER,N.COLOR_ATTACHMENT0+Ue,Ge.__webglTexture,K,Te)}}else if(T!==null&&K!==0){let Te=V.get(T.texture);N.framebufferTexture2D(N.FRAMEBUFFER,N.COLOR_ATTACHMENT0,N.TEXTURE_2D,Te.__webglTexture,K)}j=-1};function Hd(T){let O=V.get(T);return(O.__readFormat!==T.format||O.__readType!==T.type)&&(O.__readFormat=T.format,O.__readType=T.type,O.__formatReadable=C.textureFormatReadable(T.format),O.__typeReadable=C.textureTypeReadable(T.type)),O}this.readRenderTargetPixels=function(T,O,K,H,G,we,De,Te=0){if(!(T&&T.isWebGLRenderTarget)){Ye("WebGLRenderer.readRenderTargetPixels: renderTarget is not THREE.WebGLRenderTarget.");return}let Ue=V.get(T).__webglFramebuffer;if(T.isWebGLCubeRenderTarget&&De!==void 0&&(Ue=Ue[De]),Ue){v.bindFramebuffer(N.FRAMEBUFFER,Ue);try{let Ge=T.textures[Te],rt=Ge.format,dt=Ge.type;T.textures.length>1&&N.readBuffer(N.COLOR_ATTACHMENT0+Te);let Oe=Hd(Ge);if(Oe.__formatReadable===!1){Ye("WebGLRenderer.readRenderTargetPixels: renderTarget is not in RGBA or implementation defined format.");return}if(Oe.__typeReadable===!1){Ye("WebGLRenderer.readRenderTargetPixels: renderTarget is not in UnsignedByteType or implementation defined type.");return}O>=0&&O<=T.width-H&&K>=0&&K<=T.height-G&&N.readPixels(O,K,H,G,ue.convert(rt),ue.convert(dt),we)}finally{let Ge=re!==null?V.get(re).__webglFramebuffer:null;v.bindFramebuffer(N.FRAMEBUFFER,Ge)}}},this.readRenderTargetPixelsAsync=async function(T,O,K,H,G,we,De,Te=0){if(!(T&&T.isWebGLRenderTarget))throw new Error("THREE.WebGLRenderer.readRenderTargetPixels: renderTarget is not THREE.WebGLRenderTarget.");let Ue=V.get(T).__webglFramebuffer;if(T.isWebGLCubeRenderTarget&&De!==void 0&&(Ue=Ue[De]),Ue)if(O>=0&&O<=T.width-H&&K>=0&&K<=T.height-G){v.bindFramebuffer(N.FRAMEBUFFER,Ue);let Ge=T.textures[Te],rt=Ge.format,dt=Ge.type;T.textures.length>1&&N.readBuffer(N.COLOR_ATTACHMENT0+Te);let Oe=Hd(Ge);if(Oe.__formatReadable===!1)throw new Error("THREE.WebGLRenderer.readRenderTargetPixelsAsync: renderTarget is not in RGBA or implementation defined format.");if(Oe.__typeReadable===!1)throw new Error("THREE.WebGLRenderer.readRenderTargetPixelsAsync: renderTarget is not in UnsignedByteType or implementation defined type.");let Tt=N.createBuffer();N.bindBuffer(N.PIXEL_PACK_BUFFER,Tt),N.bufferData(N.PIXEL_PACK_BUFFER,we.byteLength,N.STREAM_READ),N.readPixels(O,K,H,G,ue.convert(rt),ue.convert(dt),0),N.bindBuffer(N.PIXEL_PACK_BUFFER,null);let sn=re!==null?V.get(re).__webglFramebuffer:null;v.bindFramebuffer(N.FRAMEBUFFER,sn);let Gt=N.fenceSync(N.SYNC_GPU_COMMANDS_COMPLETE,0);return N.flush(),await gp(N,Gt,4),N.bindBuffer(N.PIXEL_PACK_BUFFER,Tt),N.getBufferSubData(N.PIXEL_PACK_BUFFER,0,we),N.bindBuffer(N.PIXEL_PACK_BUFFER,null),N.deleteBuffer(Tt),N.deleteSync(Gt),we}else throw new Error("THREE.WebGLRenderer.readRenderTargetPixelsAsync: requested read bounds are out of range.")},this.copyFramebufferToTexture=function(T,O=null,K=0){let H=Math.pow(2,-K),G=Math.floor(T.image.width*H),we=Math.floor(T.image.height*H),De=O!==null?O.x:0,Te=O!==null?O.y:0;Y.setTexture2D(T,0),N.copyTexSubImage2D(N.TEXTURE_2D,K,0,0,De,Te,G,we),v.unbindTexture()},this.copyTextureToTexture=function(T,O,K=null,H=null,G=0,we=0){let De,Te,Ue,Ge,rt,dt,Oe,Tt,sn,Gt=T.isCompressedTexture?T.mipmaps[we]:T.image;if(K!==null)De=K.max.x-K.min.x,Te=K.max.y-K.min.y,Ue=K.isBox3?K.max.z-K.min.z:1,Ge=K.min.x,rt=K.min.y,dt=K.isBox3?K.min.z:0;else{let Qt=Math.pow(2,-G);De=Math.floor(Gt.width*Qt),Te=Math.floor(Gt.height*Qt),T.isDataArrayTexture?Ue=Gt.depth:T.isData3DTexture?Ue=Math.floor(Gt.depth*Qt):Ue=1,Ge=0,rt=0,dt=0}H!==null?(Oe=H.x,Tt=H.y,sn=H.z):(Oe=0,Tt=0,sn=0);let Ut=ue.convert(O.format),Sn=ue.convert(O.type),Le;O.isData3DTexture?(Y.setTexture3D(O,0),Le=N.TEXTURE_3D):O.isDataArrayTexture||O.isCompressedArrayTexture?(Y.setTexture2DArray(O,0),Le=N.TEXTURE_2D_ARRAY):(Y.setTexture2D(O,0),Le=N.TEXTURE_2D),v.activeTexture(N.TEXTURE0),v.pixelStorei(N.UNPACK_FLIP_Y_WEBGL,O.flipY),v.pixelStorei(N.UNPACK_PREMULTIPLY_ALPHA_WEBGL,O.premultiplyAlpha),v.pixelStorei(N.UNPACK_ALIGNMENT,O.unpackAlignment);let In=v.getParameter(N.UNPACK_ROW_LENGTH),_t=v.getParameter(N.UNPACK_IMAGE_HEIGHT),ei=v.getParameter(N.UNPACK_SKIP_PIXELS),wi=v.getParameter(N.UNPACK_SKIP_ROWS),ls=v.getParameter(N.UNPACK_SKIP_IMAGES);v.pixelStorei(N.UNPACK_ROW_LENGTH,Gt.width),v.pixelStorei(N.UNPACK_IMAGE_HEIGHT,Gt.height),v.pixelStorei(N.UNPACK_SKIP_PIXELS,Ge),v.pixelStorei(N.UNPACK_SKIP_ROWS,rt),v.pixelStorei(N.UNPACK_SKIP_IMAGES,dt);let hr=T.isDataArrayTexture||T.isData3DTexture,Dt=O.isDataArrayTexture||O.isData3DTexture;if(T.isDepthTexture){let Qt=V.get(T),hs=V.get(O),zt=V.get(Qt.__renderTarget),us=V.get(hs.__renderTarget);v.bindFramebuffer(N.READ_FRAMEBUFFER,zt.__webglFramebuffer),v.bindFramebuffer(N.DRAW_FRAMEBUFFER,us.__webglFramebuffer);for(let ur=0;ur<Ue;ur++)hr&&(N.framebufferTextureLayer(N.READ_FRAMEBUFFER,N.COLOR_ATTACHMENT0,V.get(T).__webglTexture,G,dt+ur),N.framebufferTextureLayer(N.DRAW_FRAMEBUFFER,N.COLOR_ATTACHMENT0,V.get(O).__webglTexture,we,sn+ur)),N.blitFramebuffer(Ge,rt,De,Te,Oe,Tt,De,Te,N.DEPTH_BUFFER_BIT,N.NEAREST);v.bindFramebuffer(N.READ_FRAMEBUFFER,null),v.bindFramebuffer(N.DRAW_FRAMEBUFFER,null)}else if(G!==0||T.isRenderTargetTexture||V.has(T)){let Qt=V.get(T),hs=V.get(O);v.bindFramebuffer(N.READ_FRAMEBUFFER,D),v.bindFramebuffer(N.DRAW_FRAMEBUFFER,k);for(let zt=0;zt<Ue;zt++)hr?N.framebufferTextureLayer(N.READ_FRAMEBUFFER,N.COLOR_ATTACHMENT0,Qt.__webglTexture,G,dt+zt):N.framebufferTexture2D(N.READ_FRAMEBUFFER,N.COLOR_ATTACHMENT0,N.TEXTURE_2D,Qt.__webglTexture,G),Dt?N.framebufferTextureLayer(N.DRAW_FRAMEBUFFER,N.COLOR_ATTACHMENT0,hs.__webglTexture,we,sn+zt):N.framebufferTexture2D(N.DRAW_FRAMEBUFFER,N.COLOR_ATTACHMENT0,N.TEXTURE_2D,hs.__webglTexture,we),G!==0?N.blitFramebuffer(Ge,rt,De,Te,Oe,Tt,De,Te,N.COLOR_BUFFER_BIT,N.NEAREST):Dt?N.copyTexSubImage3D(Le,we,Oe,Tt,sn+zt,Ge,rt,De,Te):N.copyTexSubImage2D(Le,we,Oe,Tt,Ge,rt,De,Te);v.bindFramebuffer(N.READ_FRAMEBUFFER,null),v.bindFramebuffer(N.DRAW_FRAMEBUFFER,null)}else Dt?T.isDataTexture||T.isData3DTexture?N.texSubImage3D(Le,we,Oe,Tt,sn,De,Te,Ue,Ut,Sn,Gt.data):O.isCompressedArrayTexture?N.compressedTexSubImage3D(Le,we,Oe,Tt,sn,De,Te,Ue,Ut,Gt.data):N.texSubImage3D(Le,we,Oe,Tt,sn,De,Te,Ue,Ut,Sn,Gt):T.isDataTexture?N.texSubImage2D(N.TEXTURE_2D,we,Oe,Tt,De,Te,Ut,Sn,Gt.data):T.isCompressedTexture?N.compressedTexSubImage2D(N.TEXTURE_2D,we,Oe,Tt,Gt.width,Gt.height,Ut,Gt.data):N.texSubImage2D(N.TEXTURE_2D,we,Oe,Tt,De,Te,Ut,Sn,Gt);v.pixelStorei(N.UNPACK_ROW_LENGTH,In),v.pixelStorei(N.UNPACK_IMAGE_HEIGHT,_t),v.pixelStorei(N.UNPACK_SKIP_PIXELS,ei),v.pixelStorei(N.UNPACK_SKIP_ROWS,wi),v.pixelStorei(N.UNPACK_SKIP_IMAGES,ls),we===0&&O.generateMipmaps&&N.generateMipmap(Le),v.unbindTexture()},this.initRenderTarget=function(T){V.get(T).__webglFramebuffer===void 0&&Y.setupRenderTarget(T)},this.initTexture=function(T){T.isCubeTexture?Y.setTextureCube(T,0):T.isData3DTexture?Y.setTexture3D(T,0):T.isDataArrayTexture||T.isCompressedArrayTexture?Y.setTexture2DArray(T,0):Y.setTexture2D(T,0),v.unbindTexture()},this.resetState=function(){W=0,X=0,re=null,v.reset(),be.reset()},typeof __THREE_DEVTOOLS__<"u"&&__THREE_DEVTOOLS__.dispatchEvent(new CustomEvent("observe",{detail:this}))}get coordinateSystem(){return xi}get outputColorSpace(){return this._outputColorSpace}set outputColorSpace(e){this._outputColorSpace=e;let t=this.getContext();t.drawingBufferColorSpace=lt._getDrawingBufferColorSpace(e),t.unpackColorSpace=lt._getUnpackColorSpace()}};var mo=new P;function ci(i,e,t,n,s,r){let a=2*Math.PI*s/4,o=Math.max(r-2*s,0),c=Math.PI/4;mo.copy(e),mo[n]=0,mo.normalize();let l=.5*a/(a+o),h=1-mo.angleTo(i)/c;return Math.sign(mo[t])===1?h*l:o/(a+o)+l+l*(1-h)}var Il=class i extends tn{constructor(e=1,t=1,n=1,s=2,r=.1){let a=s*2+1;if(r=Math.min(e/2,t/2,n/2,r),super(1,1,1,a,a,a),this.type="RoundedBoxGeometry",this.parameters={width:e,height:t,depth:n,segments:s,radius:r},a===1)return;let o=this.toNonIndexed();this.index=null,this.attributes.position=o.attributes.position,this.attributes.normal=o.attributes.normal,this.attributes.uv=o.attributes.uv;let c=new P,l=new P,h=new P(e,t,n).divideScalar(2).subScalar(r),u=this.attributes.position.array,d=this.attributes.normal.array,f=this.attributes.uv.array,m=u.length/6,b=new P,g=.5/a;for(let p=0,_=0;p<u.length;p+=3,_+=2)switch(c.fromArray(u,p),l.copy(c),l.x-=Math.sign(l.x)*g,l.y-=Math.sign(l.y)*g,l.z-=Math.sign(l.z)*g,l.normalize(),u[p+0]=h.x*Math.sign(c.x)+l.x*r,u[p+1]=h.y*Math.sign(c.y)+l.y*r,u[p+2]=h.z*Math.sign(c.z)+l.z*r,d[p+0]=l.x,d[p+1]=l.y,d[p+2]=l.z,Math.floor(p/m)){case 0:b.set(1,0,0),f[_+0]=ci(b,l,"z","y",r,n),f[_+1]=1-ci(b,l,"y","z",r,t);break;case 1:b.set(-1,0,0),f[_+0]=1-ci(b,l,"z","y",r,n),f[_+1]=1-ci(b,l,"y","z",r,t);break;case 2:b.set(0,1,0),f[_+0]=1-ci(b,l,"x","z",r,e),f[_+1]=ci(b,l,"z","x",r,n);break;case 3:b.set(0,-1,0),f[_+0]=1-ci(b,l,"x","z",r,e),f[_+1]=1-ci(b,l,"z","x",r,n);break;case 4:b.set(0,0,1),f[_+0]=1-ci(b,l,"x","y",r,e),f[_+1]=1-ci(b,l,"y","x",r,t);break;case 5:b.set(0,0,-1),f[_+0]=ci(b,l,"x","y",r,e),f[_+1]=1-ci(b,l,"y","x",r,t);break}}static fromJSON(e){return new i(e.width,e.height,e.depth,e.segments,e.radius)}};function go(i,e=!1){let t=i[0].index!==null,n=new Set(Object.keys(i[0].attributes)),s=new Set(Object.keys(i[0].morphAttributes)),r={},a={},o=i[0].morphTargetsRelative,c=new Bt,l=0;for(let h=0;h<i.length;++h){let u=i[h],d=0;if(t!==(u.index!==null))return console.error("THREE.BufferGeometryUtils: .mergeGeometries() failed with geometry at index "+h+". All geometries must have compatible attributes; make sure index attribute exists among all geometries, or in none of them."),null;for(let f in u.attributes){if(!n.has(f))return console.error("THREE.BufferGeometryUtils: .mergeGeometries() failed with geometry at index "+h+'. All geometries must have compatible attributes; make sure "'+f+'" attribute exists among all geometries, or in none of them.'),null;r[f]===void 0&&(r[f]=[]),r[f].push(u.attributes[f]),d++}if(d!==n.size)return console.error("THREE.BufferGeometryUtils: .mergeGeometries() failed with geometry at index "+h+". Make sure all geometries have the same number of attributes."),null;if(o!==u.morphTargetsRelative)return console.error("THREE.BufferGeometryUtils: .mergeGeometries() failed with geometry at index "+h+". .morphTargetsRelative must be consistent throughout all geometries."),null;for(let f in u.morphAttributes){if(!s.has(f))return console.error("THREE.BufferGeometryUtils: .mergeGeometries() failed with geometry at index "+h+".  .morphAttributes must be consistent throughout all geometries."),null;a[f]===void 0&&(a[f]=[]),a[f].push(u.morphAttributes[f])}if(e){let f;if(t)f=u.index.count;else if(u.attributes.position!==void 0)f=u.attributes.position.count;else return console.error("THREE.BufferGeometryUtils: .mergeGeometries() failed with geometry at index "+h+". The geometry must have either an index or a position attribute"),null;c.addGroup(l,f,h),l+=f}}if(t){let h=0,u=[];for(let d=0;d<i.length;++d){let f=i[d].index;for(let m=0;m<f.count;++m)u.push(f.getX(m)+h);h+=i[d].attributes.position.count}c.setIndex(u)}for(let h in r){let u=e0(r[h]);if(!u)return console.error("THREE.BufferGeometryUtils: .mergeGeometries() failed while trying to merge the "+h+" attribute."),null;c.setAttribute(h,u)}for(let h in a){let u=a[h][0].length;if(u!==0){c.morphAttributes=c.morphAttributes||{},c.morphAttributes[h]=[];for(let d=0;d<u;++d){let f=[];for(let b=0;b<a[h].length;++b)f.push(a[h][b][d]);let m=e0(f);if(!m)return console.error("THREE.BufferGeometryUtils: .mergeGeometries() failed while trying to merge the "+h+" morphAttribute."),null;c.morphAttributes[h].push(m)}}}return c}function e0(i){let e,t,n,s=-1,r=0;for(let l=0;l<i.length;++l){let h=i[l];if(e===void 0&&(e=h.array.constructor),e!==h.array.constructor)return console.error("THREE.BufferGeometryUtils: .mergeAttributes() failed. BufferAttribute.array must be of consistent array types across matching attributes."),null;if(t===void 0&&(t=h.itemSize),t!==h.itemSize)return console.error("THREE.BufferGeometryUtils: .mergeAttributes() failed. BufferAttribute.itemSize must be consistent across matching attributes."),null;if(n===void 0&&(n=h.normalized),n!==h.normalized)return console.error("THREE.BufferGeometryUtils: .mergeAttributes() failed. BufferAttribute.normalized must be consistent across matching attributes."),null;if(s===-1&&(s=h.gpuType),s!==h.gpuType)return console.error("THREE.BufferGeometryUtils: .mergeAttributes() failed. BufferAttribute.gpuType must be consistent across matching attributes."),null;r+=h.count*t}let a=new e(r),o=new an(a,t,n),c=0;for(let l=0;l<i.length;++l){let h=i[l];if(h.isInterleavedBufferAttribute){let u=c/t;for(let d=0,f=h.count;d<f;d++)for(let m=0;m<t;m++){let b=h.getComponent(d,m);o.setComponent(d+u,m,b)}}else a.set(h.array,c);c+=h.count*t}return s!==void 0&&(o.gpuType=s),o}function Nu(i,e){if(e===hu)return console.warn("THREE.BufferGeometryUtils.toTrianglesDrawMode(): Geometry already defined as triangles."),i;if(e===Zr||e===co){let t=i.getIndex();if(t===null){let r=[],a=i.getAttribute("position");if(a!==void 0){for(let o=0;o<a.count;o++)r.push(o);i.setIndex(r),t=i.getIndex()}else return console.error("THREE.BufferGeometryUtils.toTrianglesDrawMode(): Undefined position attribute. Processing not possible."),i}let n=t.count-2,s=[];if(e===Zr)for(let r=1;r<=n;r++)s.push(t.getX(0)),s.push(t.getX(r)),s.push(t.getX(r+1));else for(let r=0;r<n;r++)r%2===0?(s.push(t.getX(r)),s.push(t.getX(r+1)),s.push(t.getX(r+2))):(s.push(t.getX(r+2)),s.push(t.getX(r+1)),s.push(t.getX(r)));return s.length/3!==n&&console.error("THREE.BufferGeometryUtils.toTrianglesDrawMode(): Unable to generate correct amount of triangles."),i.setIndex(s),i.clearGroups(),i}else return console.error("THREE.BufferGeometryUtils.toTrianglesDrawMode(): Unknown draw mode:",e),i}function he(i,e,t,n,s,r,a,o,c=0){let l=Math.min(c,e*.49,t*.49,n*.49),h=l>0?new Il(e,t,n,1,l):new tn(e,t,n),u=new ke(h,o);return u.position.set(s,r,a),i.add(u),u}function gn(i){let e=new Map;for(let t of i.children)!t.isMesh||t.isInstancedMesh||Array.isArray(t.material)||(e.has(t.material)||e.set(t.material,[]),e.get(t.material).push(t));for(let[t,n]of e){if(n.length<2)continue;let s=n.map(a=>(a.updateMatrix(),(a.geometry.index?a.geometry.toNonIndexed():a.geometry.clone()).applyMatrix4(a.matrix))),r=go(s);for(let a of s)a.dispose();if(r){for(let a of n)i.remove(a),a.geometry.dispose();i.add(new ke(r,t))}}}function We(i,e,t,n,s,r,a,o=16){let c=new ke(new Ua(e,e,t,o),a);return c.position.set(n,s,r),i.add(c),c}function $(i,e,t,n=0,s=0,r=new Jt){let a=n-i/2,o=s-e/2,c=a+i,l=o+e;return t=Math.min(t,i/2,e/2),r.moveTo(a+t,o),r.lineTo(c-t,o),r.quadraticCurveTo(c,o,c,o+t),r.lineTo(c,l-t),r.quadraticCurveTo(c,l,c-t,l),r.lineTo(a+t,l),r.quadraticCurveTo(a,l,a,l-t),r.lineTo(a,o+t),r.quadraticCurveTo(a,o,a+t,o),r.closePath(),r}function Vt(i,e,t=0,n=0){let s=new Qe;return s.absarc(t,n,e,0,Math.PI*2,!0),i.holes.push(s),s}function Re(i,e,t,n,s,r,a,o=0,c=6,l=2){let h=new ys(e,{depth:t,curveSegments:c,bevelEnabled:o>0,bevelSize:o,bevelThickness:o,bevelSegments:l});h.clearGroups(),h.translate(0,0,-t/2);let u=new ke(h,a);return u.position.set(n,s,r),i.add(u),u}function _e(i,e,t,n,s,r,a,o,c,l=0,h=6){let u=Re(i,$(e,t,n),s,r,a,o,c,l,h);return u.rotation.x=-Math.PI/2,u}function Nt(i,e,t,n,s,r,a=32){let o=new qa(e.map(([l,h])=>new ae(l,h)),a),c=new ke(o,r);return c.position.set(t,n,s),i.add(c),c}function Rn(i,e,t,n,s=28,r=7){let a=new Gr(e.map(([c,l,h])=>new P(c,l,h))),o=new ke(new Xa(a,s,t,r,!1),n);return i.add(o),o}function rr(i,e,t){let n=new Jt;n.moveTo(.18,-.05),n.quadraticCurveTo(.41,-.24,.82,-.45),n.quadraticCurveTo(.93,-.4,.97,-.16),n.quadraticCurveTo(.66,.18,.21,.15),n.closePath();let s=new ys(n,{depth:e/i,bevelEnabled:!1,curveSegments:5});s.scale(i,i,i),s.translate(0,0,-e/2);let r=Array.from({length:t},(o,c)=>s.clone().rotateZ(c*Math.PI*2/t)),a=go(r);for(let o of r)o.dispose();return s.dispose(),a}var ku=-Math.PI/2,Fu=.375,bo=1.214,xo=1.57,Ou=1.402,Bu=-.018;function Ce(i,e,t,n,s,r,a,o,c=0){return he(i,e,t,n,s,r,a,o,Math.min(e,t,n)<.045?0:c)}function Kt(i,e,t,n,s,r,a,o=0,c=6){return Re(i,e,t,n,s,r,a,e.holes.length>=3?0:o,c,1)}function zi(i,e,t,n,s,r,a,o,c,l=0,h=6){return Kt(i,$(e,t,n),s,r,a,o,c,l,h).rotateX(-Math.PI/2)}function Cn(i,e,t,n,s,r,a,o=0,c=6){let l=Kt(i,e,t,n,s,r,a,o,c);return l.rotation.y=ku,l}function _o(i,e,t,n,s,r,a,o=20){let c=We(i,e,t,n,s,r,a,o);return c.rotation.z=Math.PI/2,c}function Kn(i,e,t,n,s,r,a,o="side"){o==="side"?(_o(i,s,.006,e,t,n,r,12),Ce(i,.0015,s*.27,s*1.22,e-.0036,t,n,a).rotation.x=.48):(We(i,s,.006,e,t,n,r,12),Ce(i,s*1.22,.0015,s*.27,e,t+.0036,n,a).rotation.y=.48)}function Zt(i,e,t,n,s,r){i.holes.push($(e,t,n,s,r,new Qe))}function Uu(i,e,t,n,s,r,a){_o(i,s,r,e-r/2,t,n,a.edge,10),_o(i,s*.86,.003,e-r-.001,t,n,a.tin,10);let o=Ce(i,.0015,s*.23,s*1.08,e-r-.0033,t,n,a.dark);o.rotation.x=.55}function gy(i,e,t,n,s,r,a){Ce(i,.014,s,r,e-.011,t,n,a.dark,.004);for(let o of[-1,1])for(let c=0;c<7;c++){let l=n+(c-3)*r*.116;Ce(i,.002,.013,r*.052,e-.001,t+o*(s/2+.003),l,a.tin)}}function Dl(i,e,{id:t,x:n,y:s,z:r,axis:a,radius:o,depth:c,count:l,rate:h,blade:u=rr(o,c,l),color:d=e.fan,trails:f=!1}){let m=new at;m.position.set(n,s,r),i.add(m),a==="side"&&(m.rotation.y=ku),a==="down"&&(m.rotation.x=Math.PI/2),a==="up"&&(m.rotation.x=-Math.PI/2);let b=new at;if(m.add(b),e.rotors.push({id:t,rotor:b,rate:h}),b.add(new ke(u,d)),We(b,o*.263,c*1.55,0,0,.004,e.edge,26).rotation.x=Math.PI/2,We(b,o*.161,.005,0,0,.005+c*.78,e.trim,22).rotation.x=Math.PI/2,f){let g=new jt({color:14214381,transparent:!0,opacity:0,depthWrite:!1});e.trails.push(g);for(let p of[.12,.24]){let _=new ke(u.clone(),g);_.rotation.z=p,_.position.z=.003,b.add(_)}}return gn(b),m}function Ll(i,e,t,n,s,r,a){Cn(i,$(r,s,.021),.039,e,t,n,a.radiator,.003,5);let o=Math.floor(r/.023);for(let c=0;c<o;c++){let l=(c-(o-1)/2)*.023,h=Math.abs(l)/r;Ce(i,.017,s*(.83-.25*h),.009,e-.025,t+.007,n+l,c%5===0?a.tin:a.trim,.0025)}for(let c of[-r*.34,r*.34])Kn(i,e-.023,t-s*.36,n+c,.01,a.tin,a.dark)}function by(){let i=document.createElement("canvas");i.width=768,i.height=1024;let e=i.getContext("2d"),t=i.width,n=i.height,s=(m,b)=>[(m-Bu+bo/2)/bo*t,(Ou+xo/2-b)/xo*n],r=241155,a=()=>(r=Math.imul(1664525,r)+1013904223>>>0)/4294967296,o=e.createLinearGradient(0,0,t,n);o.addColorStop(0,"#252f36"),o.addColorStop(.53,"#243132"),o.addColorStop(1,"#263943"),e.fillStyle=o,e.fillRect(0,0,t,n);for(let m=0;m<6400;m++)e.fillStyle=m%2?"rgba(151,167,163,.08)":"rgba(6,15,19,.12)",e.fillRect(a()*t,a()*n,1+a(),1+a());let c=(m,b,g=1.5)=>{e.beginPath(),m.forEach(([p,_],S)=>{let[x,M]=s(p,_);S?e.lineTo(x,M):e.moveTo(x,M)}),e.strokeStyle=b,e.lineWidth=g,e.stroke()},l=(m,b,g=2.8)=>{let[p,_]=s(m,b);e.fillStyle="#82785b",e.beginPath(),e.arc(p,_,g,0,Math.PI*2),e.fill(),e.fillStyle="#222b30",e.beginPath(),e.arc(p,_,g*.48,0,Math.PI*2),e.fill()},h=(m,b,g,p,_)=>{let[S,x]=s(m,b);e.fillStyle=_,e.fillRect(S-g/2,x-p/2,g,p)};for(let m=0;m<32;m++){let b=1.452+m*.016,g=-.101+m*.0018,p=m%8*.009;c([[g,b],[g+.081+p,b],[g+.119+p,b-.038],[.301+m%4*.045,b-.038]],m%4?"#405052":"#566365",1.25),l(.301+m%4*.045,b-.038,1.8)}for(let m=0;m<36;m++){let b=-.563+m*.0258,g=1.01+m%8*.023;c([[b,g],[b+.049,g-.049],[b+.049,.905+m%4*.011],[b+.093,.861+m%4*.011]],m%6?"#39494c":"#526365",1.3),l(b+.093,.861+m%4*.011,2)}for(let m=0;m<18;m++){let b=.745+m*.076;c([[.552,b],[.499,b],[.43,b+.07],[.328-m%4*.05,b+.07]],"#4d5b5b",1.4),l(.328-m%4*.05,b+.07,2.5)}for(let m=0;m<255;m++){let b=-.592+a()*1.146,g=.643+a()*1.498;m%4===0?(h(b,g,5,2.4,"#7d806e"),h(b+.012,g,5,2.4,"#7d806e"),h(b+.006,g,4,2.4,"#1d252a")):l(b,g,1.2+a()*1.1)}let u="#818e8c";for(let[m,b,g,p]of[[-.225,1.732,.428,.455],[.257,1.746,.336,.638],[-.238,1.122,.602,.092],[-.087,.904,.55,.085],[.389,.917,.199,.259],[.225,1.342,.5,.089],[-.507,.745,.147,.107],[.463,.7,.15,.071]]){let[_,S]=s(m-g/2,b+p/2);e.strokeStyle="#617374",e.lineWidth=1.4,e.strokeRect(_,S,g/bo*t,p/xo*n)}e.font="11px monospace",e.fillStyle=u;for(let[m,b,g]of[[-.35,1.445,"CPU SOCKET"],[.098,2.084,"DIMM A1   A2   B1   B2"],[-.542,1.271,"PCIe \xD716"],[.13,1.285,"M.2  2280"],[-.424,.808,"PCIe \xD74"],[.354,.76,"PCH"],[.318,.679,"F_PANEL"],[-.59,.683,"HD AUDIO"],[.379,1.511,"ATX 24P"]]){let[p,_]=s(m,b);e.fillText(g,p,_)}let d=new xn(i);d.colorSpace=pt,d.anisotropy=4;let f=new mt({map:d,roughness:.7,metalness:.12});return f.userData.ownsMap=!0,f}function xy(i,e){let t=$(bo,xo,.025);Cn(i,t,.023,Fu+.011,Ou,Bu,e.pcbEdge,.002,7);let n=new Di(t,7),s=n.attributes.position,r=n.attributes.uv;for(let c=0;c<r.count;c++)r.setXY(c,s.getX(c)/bo+.5,s.getY(c)/xo+.5);let a=new ke(n,by());a.position.set(Fu-.0045,Ou,Bu),a.rotation.y=ku,i.add(a);for(let c of[.667,1.43,2.137])for(let l of[-.564,.54])Kn(i,Fu-.007,c,l,.018,e.tin,e.dark);Ll(i,.336,2.045,-.305,.147,.528,e),Ll(i,.341,1.734,-.541,.438,.107,e);for(let c=0;c<8;c++){let l=-.502+c*.061;Ce(i,.032,.036,.041,.352,1.935,l,e.edge,.004),Uu(i,.364,1.88,l,.015,.027,e)}for(let c=0;c<6;c++){let l=1.53+c*.065;Ce(i,.027,.038,.038,.352,l,-.455,e.edge,.003),Uu(i,.364,l,-.404,.012,.024,e)}for(let c=0;c<4;c++){let l=.112+c*.083;Ce(i,.067,.644,.04,.346,1.744,l,c%2?e.edge:e.dark,.006),Ce(i,.003,.554,.01,.31,1.745,l,e.pcbEdge,.001);for(let h of[1.415,2.071])Ce(i,.077,.046,.046,.347,h,l,e.radiator,.005),Ce(i,.012,.016,.03,.303,h+.01,l,e.trim,.003);if(c===1||c===3){Ce(i,.126,.578,.03,.271,1.747,l,e.pcbEdge,.003);let h=$(.05,.551,.016);Cn(i,h,.094,.263,1.745,l,e.radiator,.0035,8);for(let u=0;u<8;u++)Ce(i,.008,.038,.052,.213,1.509+u*.064,l,e.trim,.0028);Cn(i,$(.025,.522,.012),.008,.205,1.746,l,e.diffuser,.0017,8)}}for(let[c,l,h]of[[1.122,-.224,.633],[.895,-.271,.504],[.78,-.414,.202]]){Ce(i,.064,.046,h,.343,c,l,e.dark,.004),Ce(i,.003,.013,h*.88,.309,c,l,e.pcbEdge,.001);for(let u of[-h*.425,h*.425])Ce(i,.02,.05,.022,.316,c,l+u,e.radiator,.003)}Ll(i,.343,1.331,.154,.082,.556,e),Ll(i,.334,.952,.381,.205,.267,e);let o=Nt(i,[[0,0],[.056,0],[.065,.005],[.065,.014],[.06,.019],[0,.019]],.347,.767,.203,e.tin,28);o.rotation.z=Math.PI/2,_o(i,.072,.015,.358,.767,.203,e.edge,24),Ce(i,.008,.014,.054,.325,.811,.205,e.tin,.003);for(let[c,l]of[[.737,-.338],[.982,-.492],[1.487,.512],[1.254,-.455],[.806,.388]])gy(i,.368,c,l,.077,.079,e);for(let c=0;c<8;c++)Uu(i,.367,.692+c*.064,-.531+c%2*.046,.013,.027,e);for(let c=0;c<9;c++){let l=-.428+c*.113;Ce(i,.024,.037,.047,.358,.639,l,e.edge,.003);for(let h=0;h<3;h++)Ce(i,.013,.003,.0035,.342,.638+(h-1)*.009,l+.013,e.gold)}for(let c=0;c<4;c++)Ce(i,.061,.044,.06,.343,.71+c*.052,.557,e.edge,.006),Ce(i,.003,.021,.042,.31,.71+c*.052,.557,e.dark,.003)}function _y(i,e){Cn(i,$(.406,.399,.03),.03,.35,1.731,-.19,e.tin,.003,8),Cn(i,$(.317,.309,.027),.055,.311,1.731,-.19,e.radiator,.006,8);for(let r of[-.211,.211])for(let a of[-.189,.189])Ce(i,.065,.04,.043,.33,1.731+r,-.19+a,e.edge,.005),Kn(i,.294,1.731+r,-.19+a,.014,e.tin,e.dark);for(let r=0;r<19;r++){let a=(r-9)*.0192,o=.386-.019*(Math.abs(r-9)/9);zi(i,.221,o,.028,.009,.223,1.731+a,-.19,e.fin,0,2)}for(let r=0;r<4;r++){let a=.119+r*.021,o=r<2?-1:1,c=r%2,l=.156+c*.033;Rn(i,[[.307,1.731-.091,-.19+o*(a-c*.042)],[.286,1.731-.161,-.19+o*(a-c*.042)],[.225,1.731-.199,-.19+o*(a-c*.034)],[l,1.731-.179,-.19+o*(a-c*.041)],[l,1.731+.179,-.19+o*(a-c*.041)]],.0108,e.nickel,18,7),We(i,.014,.016,l,1.731+.184,-.19+o*(a-c*.041),e.nickel,16)}let s=$(.424,.421,.043);Vt(s,.184),Cn(i,s,.044,.105,1.731,-.19,e.radiator,.004,10),Dl(i,e,{id:"cpu",x:.086,y:1.731,z:-.19,axis:"side",radius:.175,depth:.02,count:9,rate:1.23});for(let r of[-.166,.166])for(let a of[-.169,.169])Kn(i,.079,1.731+r,-.19+a,.015,e.tin,e.dark);for(let r of[-.137,.137])Rn(i,[[.06,1.731+r,-.19-.198],[.093,1.731+r,-.19-.228],[.217,1.731+r,-.19-.228],[.24,1.731+r*.83,-.19-.207]],.005,e.tin,16,6);Rn(i,[[.106,1.731+.19,-.19-.183],[.139,1.731+.233,-.19-.227],[.223,1.731+.273,-.19-.202],[.338,1.731+.268,-.19-.16]],.008,e.edge,20,6)}function yy(i,e){Ce(i,.451,.17,.061,-.132,1.111,-.724,e.edge,.003);let r=$(.471,.196,.017),a=[[-.158,.061],[-.052,.074],[.057,.074],[.162,.067]];for(let[d,f]of a)Zt(r,f,.042,.006,d,.039);for(let d=0;d<9;d++)Zt(r,.027,.022,.005,-.172+d*.043,-.044);Kt(i,r,.015,-.132,1.111,-.792,e.tin,0,3).rotation.y=Math.PI;for(let[d,f]of a)Ce(i,f*.86,.031,.01,-.132-d,1.111+.039,-.784,e.dark,.0015),Ce(i,f*.7,.006,.011,-.132-d,1.111+.036,-.793,e.trim,.001);zi(i,.637,.95,.05,.027,-.046,1.111+.088,-.075-.1,e.pcbEdge,.0025,6);for(let d=0;d<33;d++){let f=-.605+d*.031;Ce(i,.527,.091,.018,-.046,1.111+.026,f,e.fin),d%4===0&&Ce(i,.535,.008,.01,-.046,1.111+.074,f,e.tin)}for(let d of[-.211,.198])Rn(i,[[-.046+d,1.111-.012,-.075-.484],[-.046+d,1.111+.002,-.075-.376],[-.046+d*.79,1.111+.014,-.075+.298],[-.046+d*.89,1.111+.025,-.075+.414],[-.046+d*.68,1.111+.061,-.075+.467]],.01,e.nickel,22,8);let o=$(.642,1.165,.051);for(let d=0;d<9;d++)Zt(o,.026,.194,.009,-.195+d*.047,-.348);let c=[[-.081,-.071,.047,.016],[.081,-.071,.047,.016],[-.081,.071,.047,.016],[.081,.071,.047,.016]];for(let[d,f,m,b]of c)Zt(o,m,b,.007,d,f);Kt(i,o,.019,-.046,1.111+.119,-.075,e.radiator,.004,7).rotation.x=-Math.PI/2;for(let d of[-.205,.212])zi(i,.028,.604,.012,.006,-.046+d,1.111+.133,-.075-.133,e.trim,.001,5),zi(i,.056,.118,.015,.005,-.046+d*.72,1.111+.132,-.075+.182,e.edge,.001,5).rotation.y=.46;for(let d of[-.261,.269])for(let f of[-.507,.055,.504])Kn(i,-.046+d,1.111+.134,-.075+f,.011,e.tin,e.dark,"top");for(let d of[-.123,.123])for(let f of[-.111,.111]){let m=-.046+d,b=-.075+f-.121;Nt(i,[[.011,0],[.026,0],[.026,.002],[.024,.005],[.011,.005],[.011,0]],m,1.111+.135,b,e.stamped,24),Kn(i,m,1.111+.139,b,.013,e.tin,e.dark,"top")}for(let d of[-1,1]){let f=new Jt;[[.15,.45],[.223,.392],[.236,.301],[.215,.28],[.193,.368],[.127,.424]].forEach(([m,b],g)=>g?f.lineTo(d*m,b):f.moveTo(d*m,b)),f.closePath(),Kt(i,f,.003,-.046,1.111+.132,-.075,e.stamped,.001,4).rotateX(-Math.PI/2)}for(let d=0;d<13;d++){let f=-.5529999999999999+d*.025;Ce(i,.041,.005,.015,.259,1.111+.082,f,e.tin),Ce(i,.02,.007,.016,.275,1.111+.079,f,e.gold)}for(let d of[-.523,.522]){let f=$(.608,.134,.035);Kt(i,f,.068,-.046,1.111+.003,-.075+d,e.edge,.005,8)}let l=$(.632,1.162,.042);for(let d of[-.347,0,.347])Vt(l,.158,0,d);Kt(i,l,.029,-.046,1.111-.033,-.075,e.edge,.003,8).rotation.x=-Math.PI/2;let h=rr(.144,.012,7);for(let[d,f]of[-.347,0,.347].entries()){let m=h.clone(),b=d===1;b&&m.rotateY(Math.PI),Dl(i,e,{id:`gpu${d+1}`,x:-.046,y:1.111-.052,z:-.075+f,axis:"down",radius:.144,depth:.012,blade:m,rate:b?-1.1:1.1})}h.dispose();let u=$(1.163,.15,.042);for(let d of[-.341,0,.342])Zt(u,.263,.048,.018,d,-.004);Cn(i,u,.026,-.367,1.111+.034,-.075,e.edge,.004,8),Cn(i,$(.524,.01,.005),.004,-.384,1.111+.092,-.075-.172,e.diffuser,.001,5);for(let d of[-.496,.479])Kn(i,-.387,1.111+.067,-.075+d,.01,e.tin,e.dark);for(let d=0;d<6;d++)Ce(i,.029,.006,.044,.253,1.111+.091,-.075-.349+d*.057,e.gold)}function vy(i,e){let s=$(.641,.609,.037);Vt(s,.28);for(let o of[-.276,.276])for(let c of[-.26,.26])Vt(s,.014,o,c);Kt(i,s,.053,-.13,1.837,-.711,e.trim,.002,16);let r=new ke(new Ms(.291,.011,10,64),e.accent);r.position.set(-.13,1.837,-.674),i.add(r),Dl(i,e,{id:"rear",x:-.13,y:1.837,z:-.669,radius:.274,depth:.022,count:7,rate:1,color:e.caseBlade,trails:!0});for(let o of[.075,.13,.185,.238,.292]){let c=new ke(new Ms(o,.0035,5,44),e.trim);c.position.set(-.13,1.837,-.793),i.add(c)}for(let o of[Math.PI/4,Math.PI*.75])Ce(i,.573,.01,.008,-.13,1.837,-.797,e.edge,.001).rotation.z=o;for(let o of[-.276,.276])for(let c of[-.26,.26]){let l=We(i,.013,.011,-.13+o,1.837+c,-.681,e.tin,16);l.rotation.x=Math.PI/2}Rn(i,[[-.13+.29,1.837-.254,-.705],[-.13+.251,1.837-.36,-.675],[.276,1.837-.398,-.623],[.356,1.837-.394,-.556]],.006,e.cableJacket,18,6),Kt(i,$(.159,.807,.023),.05,.357,1.698,-.723,e.radiator,.004,8);for(let[o,c,l]of[[2.032,.09,.114],[1.895,.103,.112],[1.755,.126,.112],[1.595,.1,.103],[1.452,.106,.111]]){let h=$(l,c,.011);Zt(h,l-.018,c-.02,.007,0,0),Kt(i,h,.028,.359,o,-.799,e.tin,0,4).rotation.y=Math.PI,Kt(i,$(l-.022,c-.022,.005),.009,.359,o,-.782,e.dark,0,3).rotation.y=Math.PI,Ce(i,l*.68,.011,.012,.359,o-.012,-.794,o===1.755?e.trim:e.blue,.001)}for(let o=0;o<6;o++){let c=.663+o*.06,l=$(.451,.045,.008);for(let h=0;h<5;h++)Zt(l,.062,.013,.003,-.16+h*.08,0);Kt(i,l,.011,-.132,c,-.796,e.radiator,0,2)}let a=$(.065,.44,.016);for(let o=0;o<6;o++)Vt(a,.01,0,-.184+o*.06);Kt(i,a,.013,-.373,.847,-.76,e.stamped,0,6);for(let o=0;o<6;o++){let c=.663+o*.06;We(i,.013,.009,-.374,c,-.751,e.tin,16).rotateX(Math.PI/2),Ce(i,.016,.002,.001,-.374,c,-.745,e.dark).rotation.z=.5}}function My(i,e){zi(i,.646,.584,.03,.023,-.038,.118,-.354,e.edge,.005,6);for(let c of[-.355,.279])Ce(i,.014,.243,.579,c,.251,-.354,e.edge,.004);Ce(i,.641,.244,.024,-.038,.252,-.636,e.edge,.004),Ce(i,.575,.258,.082,-.038,.251,-.71,e.edge,.004);let t=$(.577,.26,.022);Zt(t,.166,.102,.009,-.168,0),Zt(t,.061,.093,.009,-.036,.004);for(let c=0;c<3;c++)for(let l=0;l<4;l++)Zt(t,.037,.023,.006,.047+l*.055,-.052+c*.05);Kt(i,t,.015,-.037,.253,-.793,e.trim,0,3).rotation.y=Math.PI,Ce(i,.155,.091,.007,.131,.253,-.788,e.dark,.003);for(let[c,l]of[[.093,.255],[.166,.255],[.13,.28]])Ce(i,.009,.022,.013,c,l,-.797,e.tin,.001);Ce(i,.05,.078,.013,-.001,.257,-.795,e.dark,.005).rotation.x=.1;for(let c=0;c<7;c++)Ce(i,.204,.054,.012,-.042,.277,-.554+c*.061,e.trim,.001);let n=$(.643,.579,.025);Vt(n,.19),Kt(i,n,.017,-.038,.38,-.354,e.radiator,.002,8).rotation.x=-Math.PI/2,Dl(i,e,{id:"psu",x:-.038,y:.377,z:-.354,axis:"up",radius:.178,depth:.013,count:7,rate:.62}),Cn(i,$(.481,.145,.015),.003,-.363,.277,-.361,e.trim,.001,6);for(let c of[-.574,-.149])for(let l of[.229,.324])Kn(i,-.368,l,c,.008,e.tin,e.dark);for(let c=0;c<14;c++)Ce(i,.003,.029,.009,-.367,.252,-.532+c*.027,e.dark,.001);Ce(i,.003,.006,.394,-.368,.301,-.358,e.radiator,.001);for(let c of[-.571,-.128])Kn(i,-.365,.18,c,.01,e.tin,e.dark);Ce(i,.52,.213,.037,-.035,.252,-.047,e.radiator,.005);for(let c of[-.208,-.089,.045,.177])for(let l of[.224,.294]){Ce(i,.088,.041,.014,c,l,-.023,e.dark,.002);for(let h=0;h<4;h++)Ce(i,.006,.003,.006,c+(h-1.5)*.015,l,-.014,e.trim)}for(let c of[.194,.31])zi(i,.465,.36,.038,.055,.032,c,.395,e.radiator,.0045,6),Ce(i,.478,.046,.037,.031,c,.594,e.edge,.004),Ce(i,.041,.011,.014,.197,c,.615,e.diffuser,.002);let s=$(1.406,.344,.019);Zt(s,.56,.189,.028,-.362,-.01);for(let c=0;c<13;c++)Zt(s,.01,.054,.004,-.033+c*.022,.068);Cn(i,s,.035,-.458,.32,0,e.radiator,0,4);let r=$(.611,.235,.024);Zt(r,.559,.191,.026,0,0),Cn(i,r,.007,-.48,.31,-.362,e.trim,0,5);let a=$(.891,1.415,.038);for(let c=0;c<13;c++)Zt(a,.018,.243,.008,-.336+c*.052,.345);Zt(a,.242,.124,.047,-.152,-.455),Zt(a,.138,.168,.046,.302,-.523),Kt(i,a,.032,-.004,.495,0,e.radiator,0,5).rotation.x=-Math.PI/2;for(let[c,l,h,u]of[[-.152,.455,.276,.151],[.302,.523,.166,.194]]){let d=$(h,u,.047);Zt(d,h-.049,u-.048,.032,0,0),Kt(i,d,.014,c,.519,l,e.edge,.001,4).rotation.x=-Math.PI/2}for(let c of[-.653,.678])for(let l of[-.381,.374])Kn(i,l,.515,c,.012,e.tin,e.dark,"top");let o=$(.741,.304,.026);Zt(o,.697,.261,.018,0,0),Kt(i,o,.01,-.025,.516,.345,e.stamped,0,6).rotateX(-Math.PI/2);for(let c of[.2,.492])for(let l of[-.328,.277])Kn(i,l,.522,c,.008,e.tin,e.dark,"top");for(let c of[-.433,.433])Ce(i,.015,.018,1.328,c,.486,0,e.coating,.003)}function Sy(i,e){let t=e.edge,n=e.cableJacket;Ce(i,.088,.067,.113,.321,1.753,.547,e.edge,.007),Ce(i,.044,.036,.106,.273,1.752,.55,e.radiator,.005);for(let r=0;r<11;r++){let a=(r-5)*.0094;Rn(i,[[.27,1.751+a,.544],[.219,1.751+a,.593],[.166,1.672+a,.637],[.192,1.536+a,.665],[.349,1.473+a,.686]],.0055,r%3===0?n:t,15,5)}Ce(i,.03,.116,.023,.176,1.614,.65,e.radiator,.003),Ce(i,.139,.044,.083,-.22,1.159,.416,e.edge,.006),Ce(i,.138,.021,.063,-.218,1.187,.429,e.radiator,.003);for(let r=0;r<8;r++){let a=(r-3.5)*.014;Rn(i,[[-.219+a,1.172,.45],[-.272+a,1.109,.515],[-.267+a,.905,.565],[-.2+a,.703,.505],[-.151+a,.5,.456]],.007,r%3===0?n:t,19,6)}let s=Ce(i,.123,.028,.031,-.24,.826,.539,e.radiator,.004);s.rotation.x=-.31,Ce(i,.05,.04,.094,.338,2.146,-.464,e.edge,.004);for(let r=0;r<6;r++){let a=(r-2.5)*.013;Rn(i,[[.323,2.155,-.469+a],[.283,2.207,-.49+a],[.288,2.256,-.534+a],[.42,2.271,-.587+a]],.005,e.cableJacket,11,5)}for(let r=0;r<5;r++){let a=(r-2)*.01;Rn(i,[[.332,.648,.453+a],[.309,.605,.468+a],[.305,.552,.501+a],[.302,.505,.533+a]],.0047,e.edge,13,5)}}function Ey(i,e){let t=$(1.416,1.685,.042);for(let r of[-.536,-.2,.157,.504])Zt(t,.087,.193,.031,.615,r);Cn(i,t,.022,.441,1.381,-.014,e.coating,0,6);for(let r of[.845,1.181,1.538,1.885]){let a=$(.108,.22,.036);Zt(a,.067,.161,.022,0,0),Cn(i,a,.018,.416,r,.601,e.gasket,.0015,7);for(let o of[-.015,.015])Ce(i,.007,.145,.012,.407,r,.601+o,e.soft).rotation.x=o*7}for(let r of[.584,2.242]){Ce(i,.043,.033,1.247,.415,r,-.022,e.stamped);for(let a of[-.55,-.24,.22,.56])Kn(i,.389,r,a,.009,e.tin,e.dark)}let n=-.322,s=.404;zi(i,.154,.117,.022,.018,n,.524,s,e.stamped,.002,7),zi(i,.122,.09,.017,.012,n,.538,s,e.gasket,.002,6),Nt(i,[[0,-.012],[.032,-.012],[.034,0],[.025,.009],[.023,.278],[.027,.286],[.027,.305],[.017,.309],[0,.309]],n,.552,s,e.coating,26),We(i,.015,.194,n,.944,s,e.satin,22),Nt(i,[[.015,-.013],[.032,-.013],[.036,-.008],[.036,.012],[.032,.017],[.016,.017]],n,.858,s,e.satin,28);for(let r=0;r<28;r++){let a=r*Math.PI/14;Ce(i,.004,.019,.005,n+Math.cos(a)*.036,.86,s+Math.sin(a)*.036,e.stamped).rotation.y=-a}zi(i,.071,.06,.013,.01,n,1.042,s,e.stamped,.002,7),zi(i,.065,.054,.012,.012,n,1.053,s,e.soft,.002,7);for(let r of[-.052,.052])Kn(i,n+r,.54,s,.008,e.tin,e.dark,"top");for(let r of[-1,1]){let a=$(.085,2.13,.022);for(let o=0;o<8;o++)Zt(a,.023,.067,.01,0,-.906+o*.259);Kt(i,a,.019,r*.383,1.253,.73,e.coating,0,4),Ce(i,.025,2.07,.039,r*.421,1.253,.736,e.stamped,.003);for(let o of[.316,.942,1.572,2.196])Ce(i,.041,.036,.046,r*.391,o,.705,e.gasket,.005),_o(i,.012,.011,r*.366,o,.705,e.satin,16)}}function Ty(i,e){for(let t of[1.542,1.919]){let n=$(.221,.371,.028);for(let s of[-.124,-.095,.095,.124])Vt(n,.013,-.042,s);Kt(i,n,.011,.218,t,-.19,e.satin,0,8).rotateX(-Math.PI/2);for(let s of[-.393,.012])Rn(i,[[.061,t,s],[.098,t+(t>1.7?.017:-.017),s],[.165,t,s],[.186,t,s+(s<0?.014:-.014)]],.0036,e.spring,16,7)}for(let t of[-.393,.012])for(let n of[-.087,-.029,.029,.087])Ce(i,.018,.008,.008,.118,1.731+n,t,e.stamped,.001);for(let t=0;t<10;t++)Ce(i,.014,.004,.006,.264,1.774,.506+t*.01,e.stamped,.001);Ce(i,.043,.011,.031,.292,1.797,.547,e.poly,.002),Ce(i,.019,.017,.027,.268,1.79,.547,e.poly,.003).rotation.z=.24;for(let t=0;t<8;t++)Ce(i,.008,.014,.025,-.269+t*.014,1.183,.466,e.stamped,.001);Ce(i,.041,.01,.045,-.226,1.2,.42,e.poly,.002).rotation.x=-.17;for(let[t,n,s,r]of[[.208,1.669,.628,.18],[.2,1.554,.656,-.35],[-.232,.767,.527,-.24]]){let a=$(.128,.036,.01);for(let c=0;c<8;c++)Vt(a,.0042,-.049+c*.014,0);let o=Kt(i,a,.009,t,n,s,e.soft,0,4);o.rotation.z=r}for(let t of[-.515,-.327,-.138,.049,.236,.423]){let n=$(.064,.031,.009);Vt(n,.006),Cn(i,n,.008,-.388,1.221,t,e.satin,0,5)}for(let t of[1.092,1.167])Cn(i,$(1.024,.014,.006),.005,-.384,t,-.082,e.stamped,0,5)}function wy(i,e){for(let t of[.194,.31]){for(let s of[-.19,.253])Ce(i,.02,.059,.393,s,t,.399,e.stamped,.003),Ce(i,.017,.021,.318,s-.011,t-.02,.395,e.satin);let n=$(.249,.038,.015);Zt(n,.202,.018,.008,0,0),Kt(i,n,.011,-.018,t,.62,e.poly,.002,8);for(let s of[-.142,.106])We(i,.015,.006,s,t,.626,e.satin,16).rotateX(Math.PI/2);Kt(i,$(.06,.024,.007),.005,.183,t,.629,e.stamped,.001,5)}for(let[t,n]of[-.208,-.089,.045,.177].entries()){Ce(i,.075,.035,.042,n,.226,.008,e.poly,.003),Ce(i,.035,.008,.019,n,.247,.007,e.stamped,.002);for(let s=0;s<4;s++){let r=(s-1.5)*.014;Rn(i,[[n+r,.226,.028],[n+r,.224,.094],[n+r*.9,.213,.144],[.04+t*.066+r,.21+t*.013,.255],[.055+t*.055+r,.193+t*.018,.352]],.0042,s===3?e.cableJacket:e.soft,18,6)}}}function t0(i,{edge:e,trim:t,dark:n,aluminum:s,portBlue:r,accent:a,fanBlade:o},c,l){let h=(d,f,m)=>new mt({color:d,metalness:f,roughness:m}),u={edge:e,trim:t,dark:n,blue:r,tin:s,accent:a,caseBlade:o,rotors:c,trails:l,pcbEdge:h(4283474,.28,.59),radiator:h(3554887,.4,.53),fin:h(9082527,.64,.48),nickel:h(10453356,.68,.45),diffuser:new mt({color:8767195,emissive:3569290,emissiveIntensity:.5,roughness:.43}),fan:h(5530996,.32,.53),gold:h(11177825,.56,.49),cableJacket:h(3951707,.14,.71),coating:h(2699064,.22,.64),stamped:h(5398637,.5,.49),satin:h(9148063,.67,.38),spring:h(10792368,.81,.33),poly:h(2634816,.13,.53),soft:h(1844271,.04,.77),gasket:h(3622224,.06,.78)};My(i,u),xy(i,u),_y(i,u),yy(i,u),vy(i,u),Sy(i,u),Ey(i,u),Ty(i,u),wy(i,u)}function Nl(i,e,t,n,s,{shell:r,dark:a,metal:o,trim:c,portBlue:l}){let h=new at;h.position.set(t,n,s),h.scale.setScalar(e/2.64),i.add(h);let u=$(2.43,1.331,.104);for(let x of[-1,1])u.holes.push($(.293,.066,.012,x*.967,-.401,new Qe));Re(h,u,.031,0,-.01,-.034,r,.003,10);let d=$(1.525,.915,.139);d.holes.push($(1.371,.089,.028,0,.331,new Qe)),Re(h,d,.068,0,-.096,-.064,c,.009,9);let f=$(1.412,.124,.049);for(let x=0;x<23;x++)f.holes.push($(.018,.065,.008,-.602+x*.0547,0,new Qe));Re(h,f,.012,0,.235,-.11,c,0,4).rotation.y=Math.PI,he(h,1.318,.082,.005,0,.235,-.081,a,.003);let m=$(1.28,.199,.031),b=[[-.463,.185,.094],[-.218,.139,.062],[.02,.149,.065],[.24,.094,.039],[.453,.122,.059]];for(let[x,M,E]of b)m.holes.push($(M,E,.007,x,0,new Qe));let g=Re(h,m,.018,0,-.472,-.075,a,0,4);g.rotation.x=Math.PI/2;for(let[x,[M,E,A]]of b.entries()){let y=$(E,A,.008);y.holes.push($(E-.013,A-.012,.005,0,0,new Qe)),Re(h,y,.016,M,-.479,-.075,o,0,4).rotation.x=Math.PI/2,he(h,E*.71,.007,A*.34,M,-.469,-.076,x===4?l:c,.001)}for(let[x,M]of[[-.17,-.065],[.17,-.065],[-.17,-.359],[.17,-.359]]){let E=Nt(h,[[.012,-.003],[.024,-.003],[.024,.003],[.011,.003],[.012,-.003]],x,M,-.113,o,20);E.rotation.x=-Math.PI/2,We(h,.01,.003,x,M,-.114,a,12).rotation.x=-Math.PI/2}let p=$(.505,.362,.039);p.holes.push($(.474,.332,.032,0,0,new Qe)),Re(h,p,.005,0,-.209,-.112,a,0,7);for(let x of[-1,1]){let M=$(.316,.083,.02);for(let E=0;E<8;E++)M.holes.push($(.021,.039,.008,(E-3.5)*.034,0,new Qe));Re(h,M,.011,x*.967,-.411,-.056,r,0,4),he(h,.286,.051,.004,x*.967,-.411,-.037,a,.003);for(let E of[-.122,.122])We(h,.009,.003,x*.966+E,-.475,-.056,o,12).rotation.x=-Math.PI/2,he(h,.012,.002,.002,x*.966+E,-.475,-.058,a).rotation.z=.44}Re(h,$(.061,.039,.006),.002,.672,-.333,-.116,a,0,4);let _=$(.069,.068,.02);_.holes.push($(.047,.048,.016,0,0,new Qe)),Re(h,_,.005,.57,-.41,-.117,r,0,7);let S=Nt(h,[[0,0],[.022,0],[.022,.011],[.015,.018],[0,.018]],.57,-.41,-.113,o,18);return S.rotation.x=-Math.PI/2,gn(h),h}var Hi=[[-.23,.055,.044,.003],[-.214,.097,.061,.002],[-.181,.121,.079,.001],[-.139,.132,.098,0],[-.093,.134,.116,.002],[-.045,.135,.128,.005],[.012,.145,.139,.008],[.066,.147,.14,.007],[.123,.139,.125,.003],[.174,.117,.103,-.001],[.212,.083,.071,-.004],[.237,.029,.039,-.006],[.241,.006,.031,-.006]],n0=Hi.map((i,e)=>i.slice(1).map((t,n)=>{let s=Hi[e-1],r=Hi[e+1];if(!s)return(r[n+1]-t)/(r[0]-i[0]);if(!r)return(t-s[n+1])/(i[0]-s[0]);let a=i[0]-s[0],o=r[0]-i[0],c=(t-s[n+1])/a,l=(r[n+1]-t)/o;if(c*l<=0)return 0;let h=2*o+a,u=o+2*a;return(h+u)/(h/c+u/l)}));function Is(i){let e=Hi.findIndex(c=>c[0]>=i);if(e<=0)return e===0?Hi[0]:Hi.at(-1);let t=Hi[e-1],n=Hi[e],s=n[0]-t[0],r=(i-t[0])/s,a=r*r,o=a*r;return[i,...t.slice(1).map((c,l)=>(2*o-3*a+1)*c+(o-2*a+r)*s*n0[e-1][l]+(-2*o+3*a)*n[l+1]+(o-a)*s*n0[e][l])]}function Ul(i,e){let[,t,n,s]=Is(i),r=Ht.clamp((e-s)/t,-1,1);return .031+(n-.031)*Math.sqrt(1-r*r)+.008*r*Math.sqrt(1-r*r)}function Fl(i,e,t,n,s,r,a){let o=[],c=[],l=[];for(let d=0;d<=s;d++){let f=Ht.lerp(e,t,d/s),[,m,,b]=Is(f),[g,p]=n(f,m);for(let _=0;_<=r;_++){let S=(1-Math.cos(Math.PI*_/r))/2,x=Ht.lerp(g,p,S)+b;if(o.push(x,Ul(f,x),f),c.push(_/r,d/s),d<s&&_<r){let M=d*(r+1)+_;l.push(M,M+r+1,M+1,M+1,M+r+1,M+r+2)}}}let h=new Bt;h.setAttribute("position",new nt(o,3)),h.setAttribute("uv",new nt(c,2)),h.setIndex(l),h.computeVertexNormals();let u=new ke(h,a);return i.add(u),u}function i0(i=1){let e=new Jt,t=Array.from({length:43},(n,s)=>Is(Ht.lerp(Hi[0][0],Hi.at(-1)[0],s/42)));for(let n of[1,-1]){let s=n===1?t:[...t].reverse();for(let[r,[a,o,,c]]of s.entries()){let l=(c+n*o)*i;!r&&n===1?e.moveTo(l,-a*i):e.lineTo(l,-a*i)}}return e.closePath(),e}function zu(i,e,t,n){return Rn(i,e,t,n,22,7)}function s0(i,e,t,{edge:n,keys:s,dark:r,aluminum:a,trim:o,lens:c,accent:l}){let h=new at;h.position.set(e,0,t),i.add(h);let u=new mt({color:9148838,metalness:.13,roughness:.57}),d=new mt({color:7636887,metalness:.18,roughness:.48}),f=new mt({color:3556433,roughness:.84}),m=new mt({color:10266799,roughness:.48});Re(h,i0(.98),.017,0,.013,0,n,.003,4).rotation.x=-Math.PI/2,Re(h,i0(1),.01,0,.027,0,f,.002,4).rotation.x=-Math.PI/2;let b=w=>.006+.018*Math.exp(-((w+.126)**2)/.0015);Fl(h,-.038,.24,(w,R)=>[-R,R],30,24,u),Fl(h,-.227,-.041,(w,R)=>[-R,-b(w)],26,17,d),Fl(h,-.227,-.041,(w,R)=>[b(w),R],26,17,d);let g=Fl(h,-.228,-.033,(w,R)=>[-R*.97,R*.97],24,18,r);g.position.y=-.006;for(let w of[-1,1]){let R=[-.219,-.177,-.105,-.059,.012,.078,.148,.208,.232].map(L=>{let[,F,,D]=Is(L);return[D+w*(F-.001),.032,L]});zu(h,R,.0017,o);let I=[-.226,-.188,-.146,-.119,-.085,-.045].map(L=>{let F=Is(L)[3]+w*b(L);return[F,Ul(L,F)-.001,L]});zu(h,I,.0016,n)}let p=-.128,_=.089,S=.027,x=Nt(h,[[.008,-.019],[.021,-.019],[.026,-.014],[.027,-.011],[.027,.011],[.026,.014],[.021,.019],[.008,.019]],0,_,p,a,48);x.rotation.z=Math.PI/2,We(h,.024,.022,0,_,p,s,48).rotation.z=Math.PI/2;for(let w=0;w<34;w++){let R=w*Math.PI*2/34,I=he(h,.02,.0018,.00185,0,_+Math.cos(R)*S,p+Math.sin(R)*S,o);I.rotation.x=R}let M=Ul(-.023,0)+.0015;_e(h,.025,.019,.008,.004,0,M,-.023,n,.001,7),We(h,.0032,.002,0,Ul(.003,0)+.001,.003,l,12);for(let w=0;w<9;w++){let R=-.033+w*.019,I=Is(R)[1],L=Is(R)[3],F=L-I+.001;zu(h,[[F,.037,R-.003],[F-.002,.049,R],[F+.001,.059,R+.006]],.0015,o)}for(let[w,R]of[[-.101,.12],[-.047,.03]]){let[,I,,L]=Is(w),F=Re(h,$(.044,.014,.006),.006,L-I-.001,.059,w,f,.002,7);F.rotation.y=-Math.PI/2,F.rotation.z=R}let E=$(.112,.028,.011);E.holes.push($(.054,.019,.008,0,.002,new Qe)),Re(h,E,.006,.003,.038,-.228,f,.001,7).rotation.y=Math.PI;let A=$(.057,.021,.009);A.holes.push($(.041,.013,.006,0,0,new Qe)),Re(h,A,.007,.003,.04,-.231,o,0,7).rotation.y=Math.PI,Re(h,$(.041,.013,.006),.003,.003,.04,-.226,r,0,7).rotation.y=Math.PI,he(h,.029,.003,.006,.003,.04,-.232,s,.001);for(let w of[-1,1])for(let[R,I,L]of[[-.148,.064,.089],[.137,.071,.097]]){let F=_e(h,I,L,.029,.004,w*.066,.004,R,m,.001,9);F.rotation.y=w*(R<0?.13:-.17)}let y=$(.071,.08,.026);return y.holes.push($(.025,.034,.009,0,0,new Qe)),Re(h,y,.004,0,.005,.022,r,0,7).rotation.x=-Math.PI/2,We(h,.011,.004,0,.004,.022,c,24),_e(h,.03,.055,.011,.003,-.063,.005,-.052,o,0,7),_e(h,.016,.027,.007,.004,-.063,.003,-.056,n,.001,6),We(h,.008,.004,.061,.005,-.051,f,16),gn(h),h}var r0={type:"change"},Gu={type:"start"},o0={type:"end"},Ol=new Li,a0=new Hn,Ay=Math.cos(70*Ht.DEG2RAD),fn=new P,On=2*Math.PI,It={NONE:-1,ROTATE:0,DOLLY:1,PAN:2,TOUCH_ROTATE:3,TOUCH_PAN:4,TOUCH_DOLLY_PAN:5,TOUCH_DOLLY_ROTATE:6},Hu=1e-6,Bl=class extends $a{constructor(e,t=null){super(e,t),this.state=It.NONE,this.target=new P,this.cursor=new P,this.minDistance=0,this.maxDistance=1/0,this.minZoom=0,this.maxZoom=1/0,this.minTargetRadius=0,this.maxTargetRadius=1/0,this.minPolarAngle=0,this.maxPolarAngle=Math.PI,this.minAzimuthAngle=-1/0,this.maxAzimuthAngle=1/0,this.enableDamping=!1,this.dampingFactor=.05,this.enableZoom=!0,this.zoomSpeed=1,this.enableRotate=!0,this.rotateSpeed=1,this.keyRotateSpeed=1,this.enablePan=!0,this.panSpeed=1,this.screenSpacePanning=!0,this.keyPanSpeed=7,this.zoomToCursor=!1,this.autoRotate=!1,this.autoRotateSpeed=2,this.keys={LEFT:"ArrowLeft",UP:"ArrowUp",RIGHT:"ArrowRight",BOTTOM:"ArrowDown"},this.mouseButtons={LEFT:ws.ROTATE,MIDDLE:ws.DOLLY,RIGHT:ws.PAN},this.touches={ONE:As.ROTATE,TWO:As.DOLLY_PAN},this.target0=this.target.clone(),this.position0=this.object.position.clone(),this.zoom0=this.object.zoom,this._cursorStyle="auto",this._domElementKeyEvents=null,this._lastPosition=new P,this._lastQuaternion=new ln,this._lastTargetPosition=new P,this._quat=new ln().setFromUnitVectors(e.up,new P(0,1,0)),this._quatInverse=this._quat.clone().invert(),this._spherical=new Ts,this._sphericalDelta=new Ts,this._scale=1,this._panOffset=new P,this._rotateStart=new ae,this._rotateEnd=new ae,this._rotateDelta=new ae,this._panStart=new ae,this._panEnd=new ae,this._panDelta=new ae,this._dollyStart=new ae,this._dollyEnd=new ae,this._dollyDelta=new ae,this._dollyDirection=new P,this._mouse=new ae,this._performCursorZoom=!1,this._pointers=[],this._pointerPositions={},this._controlActive=!1,this._onPointerMove=Cy.bind(this),this._onPointerDown=Ry.bind(this),this._onPointerUp=Py.bind(this),this._onContextMenu=Oy.bind(this),this._onMouseWheel=Dy.bind(this),this._onKeyDown=Ny.bind(this),this._onTouchStart=Fy.bind(this),this._onTouchMove=Uy.bind(this),this._onMouseDown=Iy.bind(this),this._onMouseMove=Ly.bind(this),this._interceptControlDown=By.bind(this),this._interceptControlUp=ky.bind(this),this.domElement!==null&&this.connect(this.domElement),this.update()}set cursorStyle(e){this._cursorStyle=e,e==="grab"?this.domElement.style.cursor="grab":this.domElement.style.cursor="auto"}get cursorStyle(){return this._cursorStyle}connect(e){super.connect(e),this.domElement.addEventListener("pointerdown",this._onPointerDown),this.domElement.addEventListener("pointercancel",this._onPointerUp),this.domElement.addEventListener("contextmenu",this._onContextMenu),this.domElement.addEventListener("wheel",this._onMouseWheel,{passive:!1}),this.domElement.getRootNode().addEventListener("keydown",this._interceptControlDown,{passive:!0,capture:!0}),this.domElement.style.touchAction="none"}disconnect(){this.state=It.NONE,this.domElement.removeEventListener("pointerdown",this._onPointerDown),this.domElement.ownerDocument.removeEventListener("pointermove",this._onPointerMove),this.domElement.ownerDocument.removeEventListener("pointerup",this._onPointerUp),this.domElement.removeEventListener("pointercancel",this._onPointerUp),this.domElement.removeEventListener("wheel",this._onMouseWheel),this.domElement.removeEventListener("contextmenu",this._onContextMenu),this.stopListenToKeyEvents();let e=this.domElement.getRootNode();e.removeEventListener("keydown",this._interceptControlDown,{capture:!0}),e.removeEventListener("keyup",this._interceptControlUp,{capture:!0}),this._controlActive=!1,this._pointers.length=0,this._pointerPositions={},this.domElement.style.touchAction="",this.domElement.style.cursor="auto"}dispose(){this.disconnect()}getPolarAngle(){return this._spherical.phi}getAzimuthalAngle(){return this._spherical.theta}getDistance(){return this.object.position.distanceTo(this.target)}listenToKeyEvents(e){e.addEventListener("keydown",this._onKeyDown),this._domElementKeyEvents=e}stopListenToKeyEvents(){this._domElementKeyEvents!==null&&(this._domElementKeyEvents.removeEventListener("keydown",this._onKeyDown),this._domElementKeyEvents=null)}saveState(){this.target0.copy(this.target),this.position0.copy(this.object.position),this.zoom0=this.object.zoom}reset(){this.target.copy(this.target0),this.object.position.copy(this.position0),this.object.zoom=this.zoom0,this.object.updateProjectionMatrix(),this.dispatchEvent(r0),this.update(),this.state=It.NONE}pan(e,t){this._pan(e,t),this.update()}dollyIn(e){this._dollyIn(e),this.update()}dollyOut(e){this._dollyOut(e),this.update()}rotateLeft(e){this._rotateLeft(e),this.update()}rotateUp(e){this._rotateUp(e),this.update()}update(e=null){let t=this.object.position;fn.copy(t).sub(this.target),fn.applyQuaternion(this._quat),this._spherical.setFromVector3(fn),this.autoRotate&&this.state===It.NONE&&this._rotateLeft(this._getAutoRotationAngle(e)),this.enableDamping?(this._spherical.theta+=this._sphericalDelta.theta*this.dampingFactor,this._spherical.phi+=this._sphericalDelta.phi*this.dampingFactor):(this._spherical.theta+=this._sphericalDelta.theta,this._spherical.phi+=this._sphericalDelta.phi);let n=this.minAzimuthAngle,s=this.maxAzimuthAngle;isFinite(n)&&isFinite(s)&&(n<-Math.PI?n+=On:n>Math.PI&&(n-=On),s<-Math.PI?s+=On:s>Math.PI&&(s-=On),n<=s?this._spherical.theta=Math.max(n,Math.min(s,this._spherical.theta)):this._spherical.theta=this._spherical.theta>(n+s)/2?Math.max(n,this._spherical.theta):Math.min(s,this._spherical.theta)),this._spherical.phi=Math.max(this.minPolarAngle,Math.min(this.maxPolarAngle,this._spherical.phi)),this._spherical.makeSafe(),this.enableDamping===!0?this.target.addScaledVector(this._panOffset,this.dampingFactor):this.target.add(this._panOffset),this.target.sub(this.cursor),this.target.clampLength(this.minTargetRadius,this.maxTargetRadius),this.target.add(this.cursor);let r=!1;if(this.zoomToCursor&&this._performCursorZoom||this.object.isOrthographicCamera)this._spherical.radius=this._clampDistance(this._spherical.radius);else{let a=this._spherical.radius;this._spherical.radius=this._clampDistance(this._spherical.radius*this._scale),r=a!=this._spherical.radius}if(fn.setFromSpherical(this._spherical),fn.applyQuaternion(this._quatInverse),t.copy(this.target).add(fn),this.object.lookAt(this.target),this.enableDamping===!0?(this._sphericalDelta.theta*=1-this.dampingFactor,this._sphericalDelta.phi*=1-this.dampingFactor,this._panOffset.multiplyScalar(1-this.dampingFactor)):(this._sphericalDelta.set(0,0,0),this._panOffset.set(0,0,0)),this.zoomToCursor&&this._performCursorZoom){let a=null;if(this.object.isPerspectiveCamera){let o=fn.length();a=this._clampDistance(o*this._scale);let c=o-a;this.object.position.addScaledVector(this._dollyDirection,c),this.object.updateMatrixWorld(),r=!!c}else if(this.object.isOrthographicCamera){let o=new P(this._mouse.x,this._mouse.y,0);o.unproject(this.object);let c=this.object.zoom;this.object.zoom=Math.max(this.minZoom,Math.min(this.maxZoom,this.object.zoom/this._scale)),this.object.updateProjectionMatrix(),r=c!==this.object.zoom;let l=new P(this._mouse.x,this._mouse.y,0);l.unproject(this.object),this.object.position.sub(l).add(o),this.object.updateMatrixWorld(),a=fn.length()}else console.warn("WARNING: OrbitControls.js encountered an unknown camera type - zoom to cursor disabled."),this.zoomToCursor=!1;a!==null&&(this.screenSpacePanning?this.target.set(0,0,-1).transformDirection(this.object.matrix).multiplyScalar(a).add(this.object.position):(Ol.origin.copy(this.object.position),Ol.direction.set(0,0,-1).transformDirection(this.object.matrix),Math.abs(this.object.up.dot(Ol.direction))<Ay?this.object.lookAt(this.target):(a0.setFromNormalAndCoplanarPoint(this.object.up,this.target),Ol.intersectPlane(a0,this.target))))}else if(this.object.isOrthographicCamera){let a=this.object.zoom;this.object.zoom=Math.max(this.minZoom,Math.min(this.maxZoom,this.object.zoom/this._scale)),a!==this.object.zoom&&(this.object.updateProjectionMatrix(),r=!0)}return this._scale=1,this._performCursorZoom=!1,r||this._lastPosition.distanceToSquared(this.object.position)>Hu||8*(1-this._lastQuaternion.dot(this.object.quaternion))>Hu||this._lastTargetPosition.distanceToSquared(this.target)>Hu?(this.dispatchEvent(r0),this._lastPosition.copy(this.object.position),this._lastQuaternion.copy(this.object.quaternion),this._lastTargetPosition.copy(this.target),!0):!1}_getAutoRotationAngle(e){return e!==null?On/60*this.autoRotateSpeed*e:On/60/60*this.autoRotateSpeed}_getZoomScale(e){let t=Math.abs(e*.01);return Math.pow(.95,this.zoomSpeed*t)}_rotateLeft(e){this._sphericalDelta.theta-=e}_rotateUp(e){this._sphericalDelta.phi-=e}_panLeft(e,t){fn.setFromMatrixColumn(t,0),fn.multiplyScalar(-e),this._panOffset.add(fn)}_panUp(e,t){this.screenSpacePanning===!0?fn.setFromMatrixColumn(t,1):(fn.setFromMatrixColumn(t,0),fn.crossVectors(this.object.up,fn)),fn.multiplyScalar(e),this._panOffset.add(fn)}_pan(e,t){let n=this.domElement;if(this.object.isPerspectiveCamera){let s=this.object.position;fn.copy(s).sub(this.target);let r=fn.length();r*=Math.tan(this.object.fov/2*Math.PI/180),this._panLeft(2*e*r/n.clientHeight,this.object.matrix),this._panUp(2*t*r/n.clientHeight,this.object.matrix)}else this.object.isOrthographicCamera?(this._panLeft(e*(this.object.right-this.object.left)/this.object.zoom/n.clientWidth,this.object.matrix),this._panUp(t*(this.object.top-this.object.bottom)/this.object.zoom/n.clientHeight,this.object.matrix)):(console.warn("WARNING: OrbitControls.js encountered an unknown camera type - pan disabled."),this.enablePan=!1)}_dollyOut(e){this.object.isPerspectiveCamera||this.object.isOrthographicCamera?this._scale/=e:(console.warn("WARNING: OrbitControls.js encountered an unknown camera type - dolly/zoom disabled."),this.enableZoom=!1)}_dollyIn(e){this.object.isPerspectiveCamera||this.object.isOrthographicCamera?this._scale*=e:(console.warn("WARNING: OrbitControls.js encountered an unknown camera type - dolly/zoom disabled."),this.enableZoom=!1)}_updateZoomParameters(e,t){if(!this.zoomToCursor)return;this._performCursorZoom=!0;let n=this.domElement.getBoundingClientRect(),s=e-n.left,r=t-n.top,a=n.width,o=n.height;this._mouse.x=s/a*2-1,this._mouse.y=-(r/o)*2+1,this._dollyDirection.set(this._mouse.x,this._mouse.y,1).unproject(this.object).sub(this.object.position).normalize()}_clampDistance(e){return Math.max(this.minDistance,Math.min(this.maxDistance,e))}_handleMouseDownRotate(e){this._rotateStart.set(e.clientX,e.clientY)}_handleMouseDownDolly(e){this._updateZoomParameters(e.clientX,e.clientX),this._dollyStart.set(e.clientX,e.clientY)}_handleMouseDownPan(e){this._panStart.set(e.clientX,e.clientY)}_handleMouseMoveRotate(e){this._rotateEnd.set(e.clientX,e.clientY),this._rotateDelta.subVectors(this._rotateEnd,this._rotateStart).multiplyScalar(this.rotateSpeed);let t=this.domElement;this._rotateLeft(On*this._rotateDelta.x/t.clientHeight),this._rotateUp(On*this._rotateDelta.y/t.clientHeight),this._rotateStart.copy(this._rotateEnd),this.update()}_handleMouseMoveDolly(e){this._dollyEnd.set(e.clientX,e.clientY),this._dollyDelta.subVectors(this._dollyEnd,this._dollyStart),this._dollyDelta.y>0?this._dollyOut(this._getZoomScale(this._dollyDelta.y)):this._dollyDelta.y<0&&this._dollyIn(this._getZoomScale(this._dollyDelta.y)),this._dollyStart.copy(this._dollyEnd),this.update()}_handleMouseMovePan(e){this._panEnd.set(e.clientX,e.clientY),this._panDelta.subVectors(this._panEnd,this._panStart).multiplyScalar(this.panSpeed),this._pan(this._panDelta.x,this._panDelta.y),this._panStart.copy(this._panEnd),this.update()}_handleMouseWheel(e){this._updateZoomParameters(e.clientX,e.clientY),e.deltaY<0?this._dollyIn(this._getZoomScale(e.deltaY)):e.deltaY>0&&this._dollyOut(this._getZoomScale(e.deltaY)),this.update()}_handleKeyDown(e){let t=!1;switch(e.code){case this.keys.UP:e.ctrlKey||e.metaKey||e.shiftKey?this.enableRotate&&this._rotateUp(On*this.keyRotateSpeed/this.domElement.clientHeight):this.enablePan&&this._pan(0,this.keyPanSpeed),t=!0;break;case this.keys.BOTTOM:e.ctrlKey||e.metaKey||e.shiftKey?this.enableRotate&&this._rotateUp(-On*this.keyRotateSpeed/this.domElement.clientHeight):this.enablePan&&this._pan(0,-this.keyPanSpeed),t=!0;break;case this.keys.LEFT:e.ctrlKey||e.metaKey||e.shiftKey?this.enableRotate&&this._rotateLeft(On*this.keyRotateSpeed/this.domElement.clientHeight):this.enablePan&&this._pan(this.keyPanSpeed,0),t=!0;break;case this.keys.RIGHT:e.ctrlKey||e.metaKey||e.shiftKey?this.enableRotate&&this._rotateLeft(-On*this.keyRotateSpeed/this.domElement.clientHeight):this.enablePan&&this._pan(-this.keyPanSpeed,0),t=!0;break}t&&(e.preventDefault(),this.update())}_handleTouchStartRotate(e){if(this._pointers.length===1)this._rotateStart.set(e.pageX,e.pageY);else{let t=this._getSecondPointerPosition(e),n=.5*(e.pageX+t.x),s=.5*(e.pageY+t.y);this._rotateStart.set(n,s)}}_handleTouchStartPan(e){if(this._pointers.length===1)this._panStart.set(e.pageX,e.pageY);else{let t=this._getSecondPointerPosition(e),n=.5*(e.pageX+t.x),s=.5*(e.pageY+t.y);this._panStart.set(n,s)}}_handleTouchStartDolly(e){let t=this._getSecondPointerPosition(e),n=e.pageX-t.x,s=e.pageY-t.y,r=Math.sqrt(n*n+s*s);this._dollyStart.set(0,r)}_handleTouchStartDollyPan(e){this.enableZoom&&this._handleTouchStartDolly(e),this.enablePan&&this._handleTouchStartPan(e)}_handleTouchStartDollyRotate(e){this.enableZoom&&this._handleTouchStartDolly(e),this.enableRotate&&this._handleTouchStartRotate(e)}_handleTouchMoveRotate(e){if(this._pointers.length==1)this._rotateEnd.set(e.pageX,e.pageY);else{let n=this._getSecondPointerPosition(e),s=.5*(e.pageX+n.x),r=.5*(e.pageY+n.y);this._rotateEnd.set(s,r)}this._rotateDelta.subVectors(this._rotateEnd,this._rotateStart).multiplyScalar(this.rotateSpeed);let t=this.domElement;this._rotateLeft(On*this._rotateDelta.x/t.clientHeight),this._rotateUp(On*this._rotateDelta.y/t.clientHeight),this._rotateStart.copy(this._rotateEnd)}_handleTouchMovePan(e){if(this._pointers.length===1)this._panEnd.set(e.pageX,e.pageY);else{let t=this._getSecondPointerPosition(e),n=.5*(e.pageX+t.x),s=.5*(e.pageY+t.y);this._panEnd.set(n,s)}this._panDelta.subVectors(this._panEnd,this._panStart).multiplyScalar(this.panSpeed),this._pan(this._panDelta.x,this._panDelta.y),this._panStart.copy(this._panEnd)}_handleTouchMoveDolly(e){let t=this._getSecondPointerPosition(e),n=e.pageX-t.x,s=e.pageY-t.y,r=Math.sqrt(n*n+s*s);this._dollyEnd.set(0,r),this._dollyDelta.set(0,Math.pow(this._dollyEnd.y/this._dollyStart.y,this.zoomSpeed)),this._dollyOut(this._dollyDelta.y),this._dollyStart.copy(this._dollyEnd);let a=(e.pageX+t.x)*.5,o=(e.pageY+t.y)*.5;this._updateZoomParameters(a,o)}_handleTouchMoveDollyPan(e){this.enableZoom&&this._handleTouchMoveDolly(e),this.enablePan&&this._handleTouchMovePan(e)}_handleTouchMoveDollyRotate(e){this.enableZoom&&this._handleTouchMoveDolly(e),this.enableRotate&&this._handleTouchMoveRotate(e)}_addPointer(e){this._pointers.push(e.pointerId)}_removePointer(e){delete this._pointerPositions[e.pointerId];for(let t=0;t<this._pointers.length;t++)if(this._pointers[t]==e.pointerId){this._pointers.splice(t,1);return}}_isTrackingPointer(e){for(let t=0;t<this._pointers.length;t++)if(this._pointers[t]==e.pointerId)return!0;return!1}_trackPointer(e){let t=this._pointerPositions[e.pointerId];t===void 0&&(t=new ae,this._pointerPositions[e.pointerId]=t),t.set(e.pageX,e.pageY)}_getSecondPointerPosition(e){let t=e.pointerId===this._pointers[0]?this._pointers[1]:this._pointers[0];return this._pointerPositions[t]}_customWheelEvent(e){let t=e.deltaMode,n={clientX:e.clientX,clientY:e.clientY,deltaY:e.deltaY};switch(t){case 1:n.deltaY*=16;break;case 2:n.deltaY*=100;break}return e.ctrlKey&&!this._controlActive&&(n.deltaY*=10),n}};function Ry(i){this.enabled!==!1&&(this._pointers.length===0&&(this.domElement.setPointerCapture(i.pointerId),this.domElement.ownerDocument.addEventListener("pointermove",this._onPointerMove),this.domElement.ownerDocument.addEventListener("pointerup",this._onPointerUp)),!this._isTrackingPointer(i)&&(this._addPointer(i),i.pointerType==="touch"?this._onTouchStart(i):this._onMouseDown(i),this._cursorStyle==="grab"&&(this.domElement.style.cursor="grabbing")))}function Cy(i){this.enabled!==!1&&(i.pointerType==="touch"?this._onTouchMove(i):this._onMouseMove(i))}function Py(i){switch(this._removePointer(i),this._pointers.length){case 0:this.domElement.releasePointerCapture(i.pointerId),this.domElement.ownerDocument.removeEventListener("pointermove",this._onPointerMove),this.domElement.ownerDocument.removeEventListener("pointerup",this._onPointerUp),this.dispatchEvent(o0),this.state=It.NONE,this._cursorStyle==="grab"&&(this.domElement.style.cursor="grab");break;case 1:let e=this._pointers[0],t=this._pointerPositions[e];this._onTouchStart({pointerId:e,pageX:t.x,pageY:t.y});break}}function Iy(i){let e;switch(i.button){case 0:e=this.mouseButtons.LEFT;break;case 1:e=this.mouseButtons.MIDDLE;break;case 2:e=this.mouseButtons.RIGHT;break;default:e=-1}switch(e){case ws.DOLLY:if(this.enableZoom===!1)return;this._handleMouseDownDolly(i),this.state=It.DOLLY;break;case ws.ROTATE:if(i.ctrlKey||i.metaKey||i.shiftKey){if(this.enablePan===!1)return;this._handleMouseDownPan(i),this.state=It.PAN}else{if(this.enableRotate===!1)return;this._handleMouseDownRotate(i),this.state=It.ROTATE}break;case ws.PAN:if(i.ctrlKey||i.metaKey||i.shiftKey){if(this.enableRotate===!1)return;this._handleMouseDownRotate(i),this.state=It.ROTATE}else{if(this.enablePan===!1)return;this._handleMouseDownPan(i),this.state=It.PAN}break;default:this.state=It.NONE}this.state!==It.NONE&&this.dispatchEvent(Gu)}function Ly(i){switch(this.state){case It.ROTATE:if(this.enableRotate===!1)return;this._handleMouseMoveRotate(i);break;case It.DOLLY:if(this.enableZoom===!1)return;this._handleMouseMoveDolly(i);break;case It.PAN:if(this.enablePan===!1)return;this._handleMouseMovePan(i);break}}function Dy(i){this.enabled===!1||this.enableZoom===!1||this.state!==It.NONE||(i.preventDefault(),this.dispatchEvent(Gu),this._handleMouseWheel(this._customWheelEvent(i)),this.dispatchEvent(o0))}function Ny(i){this.enabled!==!1&&this._handleKeyDown(i)}function Fy(i){switch(this._trackPointer(i),this._pointers.length){case 1:switch(this.touches.ONE){case As.ROTATE:if(this.enableRotate===!1)return;this._handleTouchStartRotate(i),this.state=It.TOUCH_ROTATE;break;case As.PAN:if(this.enablePan===!1)return;this._handleTouchStartPan(i),this.state=It.TOUCH_PAN;break;default:this.state=It.NONE}break;case 2:switch(this.touches.TWO){case As.DOLLY_PAN:if(this.enableZoom===!1&&this.enablePan===!1)return;this._handleTouchStartDollyPan(i),this.state=It.TOUCH_DOLLY_PAN;break;case As.DOLLY_ROTATE:if(this.enableZoom===!1&&this.enableRotate===!1)return;this._handleTouchStartDollyRotate(i),this.state=It.TOUCH_DOLLY_ROTATE;break;default:this.state=It.NONE}break;default:this.state=It.NONE}this.state!==It.NONE&&this.dispatchEvent(Gu)}function Uy(i){switch(this._trackPointer(i),this.state){case It.TOUCH_ROTATE:if(this.enableRotate===!1)return;this._handleTouchMoveRotate(i),this.update();break;case It.TOUCH_PAN:if(this.enablePan===!1)return;this._handleTouchMovePan(i),this.update();break;case It.TOUCH_DOLLY_PAN:if(this.enableZoom===!1&&this.enablePan===!1)return;this._handleTouchMoveDollyPan(i),this.update();break;case It.TOUCH_DOLLY_ROTATE:if(this.enableZoom===!1&&this.enableRotate===!1)return;this._handleTouchMoveDollyRotate(i),this.update();break;default:this.state=It.NONE}}function Oy(i){this.enabled!==!1&&i.preventDefault()}function By(i){i.key==="Control"&&(this._controlActive=!0,this.domElement.getRootNode().addEventListener("keyup",this._interceptControlUp,{passive:!0,capture:!0}))}function ky(i){i.key==="Control"&&(this._controlActive=!1,this.domElement.getRootNode().removeEventListener("keyup",this._interceptControlUp,{passive:!0,capture:!0}))}function c0(i){let e=new Map,t=new Map,n=i.clone();return l0(i,n,function(s,r){e.set(r,s),t.set(s,r)}),n.traverse(function(s){if(!s.isSkinnedMesh)return;let r=s,a=e.get(s),o=a.skeleton.bones;r.skeleton=a.skeleton.clone(),r.bindMatrix.copy(a.bindMatrix),r.skeleton.bones=o.map(function(c){return t.get(c)}),r.bind(r.skeleton,r.bindMatrix)}),n}function l0(i,e,t){t(i,e);for(let n=0;n<i.children.length;n++)l0(i.children[n],e.children[n],t)}var kl=class extends Fi{constructor(e){super(e),this.dracoLoader=null,this.ktx2Loader=null,this.meshoptDecoder=null,this.pluginCallbacks=[],this.register(function(t){return new Yu(t)}),this.register(function(t){return new Ju(t)}),this.register(function(t){return new rd(t)}),this.register(function(t){return new ad(t)}),this.register(function(t){return new od(t)}),this.register(function(t){return new $u(t)}),this.register(function(t){return new Qu(t)}),this.register(function(t){return new ed(t)}),this.register(function(t){return new td(t)}),this.register(function(t){return new Ku(t)}),this.register(function(t){return new nd(t)}),this.register(function(t){return new Zu(t)}),this.register(function(t){return new sd(t)}),this.register(function(t){return new id(t)}),this.register(function(t){return new Xu(t)}),this.register(function(t){return new zl(t,ut.EXT_MESHOPT_COMPRESSION)}),this.register(function(t){return new zl(t,ut.KHR_MESHOPT_COMPRESSION)}),this.register(function(t){return new cd(t)})}load(e,t,n,s){let r=this,a;if(this.resourcePath!=="")a=this.resourcePath;else if(this.path!==""){let l=is.extractUrlBase(e);a=is.resolveURL(l,this.path)}else a=is.extractUrlBase(e);this.manager.itemStart(e);let o=function(l){s?s(l):console.error(l),r.manager.itemError(e),r.manager.itemEnd(e)},c=new Wr(this.manager);c.setPath(this.path),c.setResponseType("arraybuffer"),c.setRequestHeader(this.requestHeader),c.setWithCredentials(this.withCredentials),c.load(e,function(l){try{r.parse(l,a,function(h){t(h),r.manager.itemEnd(e)},o)}catch(h){o(h)}},n,o)}setDRACOLoader(e){return this.dracoLoader=e,this}setKTX2Loader(e){return this.ktx2Loader=e,this}setMeshoptDecoder(e){return this.meshoptDecoder=e,this}register(e){return this.pluginCallbacks.indexOf(e)===-1&&this.pluginCallbacks.push(e),this}unregister(e){return this.pluginCallbacks.indexOf(e)!==-1&&this.pluginCallbacks.splice(this.pluginCallbacks.indexOf(e),1),this}parse(e,t,n,s){let r,a={},o={},c=new TextDecoder;if(typeof e=="string")r=JSON.parse(e);else if(e instanceof ArrayBuffer)if(c.decode(new Uint8Array(e,0,4))===p0){try{a[ut.KHR_BINARY_GLTF]=new ld(e)}catch(u){s&&s(u);return}r=JSON.parse(a[ut.KHR_BINARY_GLTF].content)}else r=JSON.parse(c.decode(e));else r=e;if(r.asset===void 0||r.asset.version[0]<2){s&&s(new Error("THREE.GLTFLoader: Unsupported asset. glTF versions >=2.0 are supported."));return}let l=new gd(r,{path:t||this.resourcePath||"",crossOrigin:this.crossOrigin,requestHeader:this.requestHeader,manager:this.manager,ktx2Loader:this.ktx2Loader,meshoptDecoder:this.meshoptDecoder});l.fileLoader.setRequestHeader(this.requestHeader);for(let h=0;h<this.pluginCallbacks.length;h++){let u=this.pluginCallbacks[h](l);u.name||console.error("THREE.GLTFLoader: Invalid plugin found: missing name"),o[u.name]=u,a[u.name]=!0}if(r.extensionsUsed)for(let h=0;h<r.extensionsUsed.length;++h){let u=r.extensionsUsed[h],d=r.extensionsRequired||[];switch(u){case ut.KHR_MATERIALS_UNLIT:a[u]=new ju;break;case ut.KHR_DRACO_MESH_COMPRESSION:a[u]=new hd(r,this.dracoLoader);break;case ut.KHR_TEXTURE_TRANSFORM:a[u]=new ud;break;case ut.KHR_MESH_QUANTIZATION:a[u]=new dd;break;default:d.indexOf(u)>=0&&o[u]===void 0&&console.warn('THREE.GLTFLoader: Unknown extension "'+u+'".')}}l.setExtensions(a),l.setPlugins(o),l.parse(n,s)}parseAsync(e,t){let n=this;return new Promise(function(s,r){n.parse(e,t,s,r)})}};function zy(){let i={};return{get:function(e){return i[e]},add:function(e,t){i[e]=t},remove:function(e){delete i[e]},removeAll:function(){i={}}}}function nn(i,e,t){let n=i.json.materials[e];return n.extensions&&n.extensions[t]?n.extensions[t]:null}var ut={KHR_BINARY_GLTF:"KHR_binary_glTF",KHR_DRACO_MESH_COMPRESSION:"KHR_draco_mesh_compression",KHR_LIGHTS_PUNCTUAL:"KHR_lights_punctual",KHR_MATERIALS_CLEARCOAT:"KHR_materials_clearcoat",KHR_MATERIALS_DISPERSION:"KHR_materials_dispersion",KHR_MATERIALS_IOR:"KHR_materials_ior",KHR_MATERIALS_SHEEN:"KHR_materials_sheen",KHR_MATERIALS_SPECULAR:"KHR_materials_specular",KHR_MATERIALS_TRANSMISSION:"KHR_materials_transmission",KHR_MATERIALS_IRIDESCENCE:"KHR_materials_iridescence",KHR_MATERIALS_ANISOTROPY:"KHR_materials_anisotropy",KHR_MATERIALS_UNLIT:"KHR_materials_unlit",KHR_MATERIALS_VOLUME:"KHR_materials_volume",KHR_TEXTURE_BASISU:"KHR_texture_basisu",KHR_TEXTURE_TRANSFORM:"KHR_texture_transform",KHR_MESH_QUANTIZATION:"KHR_mesh_quantization",KHR_MATERIALS_EMISSIVE_STRENGTH:"KHR_materials_emissive_strength",EXT_MATERIALS_BUMP:"EXT_materials_bump",EXT_TEXTURE_WEBP:"EXT_texture_webp",EXT_TEXTURE_AVIF:"EXT_texture_avif",EXT_MESHOPT_COMPRESSION:"EXT_meshopt_compression",KHR_MESHOPT_COMPRESSION:"KHR_meshopt_compression",EXT_MESH_GPU_INSTANCING:"EXT_mesh_gpu_instancing"},Xu=class{constructor(e){this.parser=e,this.name=ut.KHR_LIGHTS_PUNCTUAL,this.cache={refs:{},uses:{}}}_markDefs(){let e=this.parser,t=this.parser.json.nodes||[];for(let n=0,s=t.length;n<s;n++){let r=t[n];r.extensions&&r.extensions[this.name]&&r.extensions[this.name].light!==void 0&&e._addNodeRef(this.cache,r.extensions[this.name].light)}}_loadLight(e){let t=this.parser,n="light:"+e,s=t.cache.get(n);if(s)return s;let r=t.json,c=((r.extensions&&r.extensions[this.name]||{}).lights||[])[e],l,h=new Be(16777215);c.color!==void 0&&h.setRGB(c.color[0],c.color[1],c.color[2],Dn);let u=c.range!==void 0?c.range:0;switch(c.type){case"directional":l=new yi(h),l.target.position.set(0,0,-1),l.add(l.target);break;case"point":l=new Qs(h),l.distance=u;break;case"spot":l=new Ja(h),l.distance=u,c.spot=c.spot||{},c.spot.innerConeAngle=c.spot.innerConeAngle!==void 0?c.spot.innerConeAngle:0,c.spot.outerConeAngle=c.spot.outerConeAngle!==void 0?c.spot.outerConeAngle:Math.PI/4,l.angle=c.spot.outerConeAngle,l.penumbra=1-c.spot.innerConeAngle/c.spot.outerConeAngle,l.target.position.set(0,0,-1),l.add(l.target);break;default:throw new Error("THREE.GLTFLoader: Unexpected light type: "+c.type)}return l.position.set(0,0,0),Gi(l,c),c.intensity!==void 0&&(l.intensity=c.intensity),l.name=t.createUniqueName(c.name||"light_"+e),s=Promise.resolve(l),t.cache.add(n,s),s}getDependency(e,t){if(e==="light")return this._loadLight(t)}createNodeAttachment(e){let t=this,n=this.parser,r=n.json.nodes[e],o=(r.extensions&&r.extensions[this.name]||{}).light;return o===void 0?null:this._loadLight(o).then(function(c){return n._getNodeRef(t.cache,o,c)})}},ju=class{constructor(){this.name=ut.KHR_MATERIALS_UNLIT}getMaterialType(){return jt}extendParams(e,t,n){let s=[];e.color=new Be(1,1,1),e.opacity=1;let r=t.pbrMetallicRoughness;if(r){if(Array.isArray(r.baseColorFactor)){let a=r.baseColorFactor;e.color.setRGB(a[0],a[1],a[2],Dn),e.opacity=a[3]}r.baseColorTexture!==void 0&&s.push(n.assignTexture(e,"map",r.baseColorTexture,pt))}return Promise.all(s)}},Ku=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_EMISSIVE_STRENGTH}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);return n===null||n.emissiveStrength!==void 0&&(t.emissiveIntensity=n.emissiveStrength),Promise.resolve()}},Yu=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_CLEARCOAT}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);if(n===null)return Promise.resolve();let s=[];if(n.clearcoatFactor!==void 0&&(t.clearcoat=n.clearcoatFactor),n.clearcoatTexture!==void 0&&s.push(this.parser.assignTexture(t,"clearcoatMap",n.clearcoatTexture)),n.clearcoatRoughnessFactor!==void 0&&(t.clearcoatRoughness=n.clearcoatRoughnessFactor),n.clearcoatRoughnessTexture!==void 0&&s.push(this.parser.assignTexture(t,"clearcoatRoughnessMap",n.clearcoatRoughnessTexture)),n.clearcoatNormalTexture!==void 0&&(s.push(this.parser.assignTexture(t,"clearcoatNormalMap",n.clearcoatNormalTexture)),n.clearcoatNormalTexture.scale!==void 0)){let r=n.clearcoatNormalTexture.scale;t.clearcoatNormalScale=new ae(r,r)}return Promise.all(s)}},Ju=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_DISPERSION}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);return n===null||(t.dispersion=n.dispersion!==void 0?n.dispersion:0),Promise.resolve()}},Zu=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_IRIDESCENCE}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);if(n===null)return Promise.resolve();let s=[];return n.iridescenceFactor!==void 0&&(t.iridescence=n.iridescenceFactor),n.iridescenceTexture!==void 0&&s.push(this.parser.assignTexture(t,"iridescenceMap",n.iridescenceTexture)),n.iridescenceIor!==void 0&&(t.iridescenceIOR=n.iridescenceIor),t.iridescenceThicknessRange===void 0&&(t.iridescenceThicknessRange=[100,400]),n.iridescenceThicknessMinimum!==void 0&&(t.iridescenceThicknessRange[0]=n.iridescenceThicknessMinimum),n.iridescenceThicknessMaximum!==void 0&&(t.iridescenceThicknessRange[1]=n.iridescenceThicknessMaximum),n.iridescenceThicknessTexture!==void 0&&s.push(this.parser.assignTexture(t,"iridescenceThicknessMap",n.iridescenceThicknessTexture)),Promise.all(s)}},$u=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_SHEEN}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);if(n===null)return Promise.resolve();let s=[];if(t.sheenColor=new Be(0,0,0),t.sheenRoughness=0,t.sheen=1,n.sheenColorFactor!==void 0){let r=n.sheenColorFactor;t.sheenColor.setRGB(r[0],r[1],r[2],Dn)}return n.sheenRoughnessFactor!==void 0&&(t.sheenRoughness=n.sheenRoughnessFactor),n.sheenColorTexture!==void 0&&s.push(this.parser.assignTexture(t,"sheenColorMap",n.sheenColorTexture,pt)),n.sheenRoughnessTexture!==void 0&&s.push(this.parser.assignTexture(t,"sheenRoughnessMap",n.sheenRoughnessTexture)),Promise.all(s)}},Qu=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_TRANSMISSION}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);if(n===null)return Promise.resolve();let s=[];return n.transmissionFactor!==void 0&&(t.transmission=n.transmissionFactor),n.transmissionTexture!==void 0&&s.push(this.parser.assignTexture(t,"transmissionMap",n.transmissionTexture)),Promise.all(s)}},ed=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_VOLUME}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);if(n===null)return Promise.resolve();let s=[];t.thickness=n.thicknessFactor!==void 0?n.thicknessFactor:0,n.thicknessTexture!==void 0&&s.push(this.parser.assignTexture(t,"thicknessMap",n.thicknessTexture)),t.attenuationDistance=n.attenuationDistance||1/0;let r=n.attenuationColor||[1,1,1];return t.attenuationColor=new Be().setRGB(r[0],r[1],r[2],Dn),Promise.all(s)}},td=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_IOR}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);return n===null||(t.ior=n.ior!==void 0?n.ior:1.5,t.ior===0&&(t.ior=1e3)),Promise.resolve()}},nd=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_SPECULAR}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);if(n===null)return Promise.resolve();let s=[];t.specularIntensity=n.specularFactor!==void 0?n.specularFactor:1,n.specularTexture!==void 0&&s.push(this.parser.assignTexture(t,"specularIntensityMap",n.specularTexture));let r=n.specularColorFactor||[1,1,1];return t.specularColor=new Be().setRGB(r[0],r[1],r[2],Dn),n.specularColorTexture!==void 0&&s.push(this.parser.assignTexture(t,"specularColorMap",n.specularColorTexture,pt)),Promise.all(s)}},id=class{constructor(e){this.parser=e,this.name=ut.EXT_MATERIALS_BUMP}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);if(n===null)return Promise.resolve();let s=[];return t.bumpScale=n.bumpFactor!==void 0?n.bumpFactor:1,n.bumpTexture!==void 0&&s.push(this.parser.assignTexture(t,"bumpMap",n.bumpTexture)),Promise.all(s)}},sd=class{constructor(e){this.parser=e,this.name=ut.KHR_MATERIALS_ANISOTROPY}getMaterialType(e){return nn(this.parser,e,this.name)!==null?_n:null}extendMaterialParams(e,t){let n=nn(this.parser,e,this.name);if(n===null)return Promise.resolve();let s=[];return n.anisotropyStrength!==void 0&&(t.anisotropy=n.anisotropyStrength),n.anisotropyRotation!==void 0&&(t.anisotropyRotation=n.anisotropyRotation),n.anisotropyTexture!==void 0&&s.push(this.parser.assignTexture(t,"anisotropyMap",n.anisotropyTexture)),Promise.all(s)}},rd=class{constructor(e){this.parser=e,this.name=ut.KHR_TEXTURE_BASISU}loadTexture(e){let t=this.parser,n=t.json,s=n.textures[e];if(!s.extensions||!s.extensions[this.name])return null;let r=s.extensions[this.name],a=t.options.ktx2Loader;if(!a){if(n.extensionsRequired&&n.extensionsRequired.indexOf(this.name)>=0)throw new Error("THREE.GLTFLoader: setKTX2Loader must be called before loading KTX2 textures");return null}return t.loadTextureImage(e,r.source,a)}},ad=class{constructor(e){this.parser=e,this.name=ut.EXT_TEXTURE_WEBP}loadTexture(e){let t=this.name,n=this.parser,s=n.json,r=s.textures[e];if(!r.extensions||!r.extensions[t])return null;let a=r.extensions[t],o=s.images[a.source],c=n.textureLoader;if(o.uri){let l=n.options.manager.getHandler(o.uri);l!==null&&(c=l)}return n.loadTextureImage(e,a.source,c)}},od=class{constructor(e){this.parser=e,this.name=ut.EXT_TEXTURE_AVIF}loadTexture(e){let t=this.name,n=this.parser,s=n.json,r=s.textures[e];if(!r.extensions||!r.extensions[t])return null;let a=r.extensions[t],o=s.images[a.source],c=n.textureLoader;if(o.uri){let l=n.options.manager.getHandler(o.uri);l!==null&&(c=l)}return n.loadTextureImage(e,a.source,c)}},zl=class{constructor(e,t){this.name=t,this.parser=e}loadBufferView(e){let t=this.parser.json,n=t.bufferViews[e];if(n.extensions&&n.extensions[this.name]){let s=n.extensions[this.name],r=this.parser.getDependency("buffer",s.buffer),a=this.parser.options.meshoptDecoder;if(!a||!a.supported){if(t.extensionsRequired&&t.extensionsRequired.indexOf(this.name)>=0)throw new Error("THREE.GLTFLoader: setMeshoptDecoder must be called before loading compressed files");return null}return r.then(function(o){let c=s.byteOffset||0,l=s.byteLength||0,h=s.count,u=s.byteStride,d=new Uint8Array(o,c,l);return a.decodeGltfBufferAsync?a.decodeGltfBufferAsync(h,u,d,s.mode,s.filter).then(function(f){return f.buffer}):a.ready.then(function(){let f=new ArrayBuffer(h*u);return a.decodeGltfBuffer(new Uint8Array(f),h,u,d,s.mode,s.filter),f})})}else return null}},cd=class{constructor(e){this.name=ut.EXT_MESH_GPU_INSTANCING,this.parser=e}createNodeMesh(e){let t=this.parser.json,n=t.nodes[e];if(!n.extensions||!n.extensions[this.name]||n.mesh===void 0)return null;let s=t.meshes[n.mesh];for(let l of s.primitives)if(l.mode!==li.TRIANGLES&&l.mode!==li.TRIANGLE_STRIP&&l.mode!==li.TRIANGLE_FAN&&l.mode!==void 0)return null;let a=n.extensions[this.name].attributes,o=[],c={};for(let l in a)o.push(this.parser.getDependency("accessor",a[l]).then(h=>(c[l]=h,c[l])));return o.length<1?null:(o.push(this.parser.createNodeMesh(e)),Promise.all(o).then(l=>{let h=l.pop(),u=h.isGroup?h.children:[h],d=l[0].count,f=[];for(let m of u){let b=new Ze,g=new P,p=new ln,_=new P(1,1,1),S=new ri(m.geometry,m.material,d);for(let M=0;M<d;M++)c.TRANSLATION&&g.fromBufferAttribute(c.TRANSLATION,M),c.ROTATION&&p.fromBufferAttribute(c.ROTATION,M),c.SCALE&&_.fromBufferAttribute(c.SCALE,M),S.setMatrixAt(M,b.compose(g,p,_));let x=null;for(let M in c)if(M==="_COLOR_0"){let E=c[M];S.instanceColor=new Zi(E.array,E.itemSize,E.normalized)}else if(M!=="TRANSLATION"&&M!=="ROTATION"&&M!=="SCALE"){if(x===null){let A=S.geometry;x=new Bt,x.name=A.name;for(let y in A.attributes)x.setAttribute(y,A.attributes[y]);for(let y in A.morphAttributes)x.morphAttributes[y]=A.morphAttributes[y];A.index!==null&&x.setIndex(A.index),x.morphTargetsRelative=A.morphTargetsRelative;for(let y of A.groups)x.addGroup(y.start,y.count,y.materialIndex);A.boundingBox!==null&&(x.boundingBox=A.boundingBox.clone()),A.boundingSphere!==null&&(x.boundingSphere=A.boundingSphere.clone()),x.drawRange.start=A.drawRange.start,x.drawRange.count=A.drawRange.count,x.userData=Object.assign({},A.userData),S.geometry=x}let E=c[M];x.setAttribute(M,new Zi(E.array,E.itemSize,E.normalized))}At.prototype.copy.call(S,m),this.parser.assignFinalMaterial(S),f.push(S)}return h.isGroup?(h.clear(),h.add(...f),h):f[0]}))}},p0="glTF",yo=12,h0={JSON:1313821514,BIN:5130562},ld=class{constructor(e){this.name=ut.KHR_BINARY_GLTF,this.content=null,this.body=null;let t=new DataView(e,0,yo),n=new TextDecoder;if(this.header={magic:n.decode(new Uint8Array(e.slice(0,4))),version:t.getUint32(4,!0),length:t.getUint32(8,!0)},this.header.magic!==p0)throw new Error("THREE.GLTFLoader: Unsupported glTF-Binary header.");if(this.header.version<2)throw new Error("THREE.GLTFLoader: Legacy binary file detected.");let s=this.header.length-yo,r=new DataView(e,yo),a=0;for(;a<s;){let o=r.getUint32(a,!0);a+=4;let c=r.getUint32(a,!0);if(a+=4,c===h0.JSON){let l=new Uint8Array(e,yo+a,o);this.content=n.decode(l)}else if(c===h0.BIN){let l=yo+a;this.body=e.slice(l,l+o)}a+=o}if(this.content===null)throw new Error("THREE.GLTFLoader: JSON content not found.")}},hd=class{constructor(e,t){if(!t)throw new Error("THREE.GLTFLoader: No DRACOLoader instance provided.");this.name=ut.KHR_DRACO_MESH_COMPRESSION,this.json=e,this.dracoLoader=t,this.dracoLoader.preload()}decodePrimitive(e,t){let n=this.json,s=this.dracoLoader,r=e.extensions[this.name].bufferView,a=e.extensions[this.name].attributes,o={},c={},l={};for(let h in a){let u=pd[h]||h.toLowerCase();o[u]=a[h]}for(let h in e.attributes){let u=pd[h]||h.toLowerCase();if(a[h]!==void 0){let d=n.accessors[e.attributes[h]],f=ia[d.componentType];l[u]=f.name,c[u]=d.normalized===!0}}return t.getDependency("bufferView",r).then(function(h){return new Promise(function(u,d){s.decodeDracoFile(h,function(f){for(let m in f.attributes){let b=f.attributes[m],g=c[m];g!==void 0&&(b.normalized=g)}u(f)},o,l,Dn,d)})})}},ud=class{constructor(){this.name=ut.KHR_TEXTURE_TRANSFORM}extendTexture(e,t){if((t.texCoord===void 0||t.texCoord===e.channel)&&t.offset===void 0&&t.rotation===void 0&&t.scale===void 0)return e;if(e=e.clone(),t.texCoord!==void 0&&(e.channel=t.texCoord),t.offset!==void 0&&e.offset.fromArray(t.offset),t.rotation!==void 0&&(e.rotation=t.rotation),t.scale!==void 0&&e.repeat.fromArray(t.scale),t.rotation!==void 0){let n=Math.cos(e.rotation),s=Math.sin(e.rotation);e.matrix.set(e.repeat.x*n,e.repeat.y*s,e.offset.x,-e.repeat.x*s,e.repeat.y*n,e.offset.y,0,0,1),e.matrixAutoUpdate=!1}return e.needsUpdate=!0,e}},dd=class{constructor(){this.name=ut.KHR_MESH_QUANTIZATION}},Hl=class extends Ni{constructor(e,t,n,s){super(e,t,n,s)}copySampleValue_(e){let t=this.resultBuffer,n=this.sampleValues,s=this.valueSize,r=e*s*3+s;for(let a=0;a!==s;a++)t[a]=n[r+a];return t}interpolate_(e,t,n,s){let r=this.resultBuffer,a=this.sampleValues,o=this.valueSize,c=o*2,l=o*3,h=s-t,u=(n-t)/h,d=u*u,f=d*u,m=e*l,b=m-l,g=-2*f+3*d,p=f-d,_=1-g,S=p-d+u;for(let x=0;x!==o;x++){let M=a[b+x+o],E=a[b+x+c]*h,A=a[m+x+o],y=a[m+x]*h;r[x]=_*M+S*E+g*A+p*y}return r}},Hy=new ln,fd=class extends Hl{interpolate_(e,t,n,s){let r=super.interpolate_(e,t,n,s);return Hy.fromArray(r).normalize().toArray(r),r}},li={FLOAT:5126,FLOAT_MAT3:35675,FLOAT_MAT4:35676,FLOAT_VEC2:35664,FLOAT_VEC3:35665,FLOAT_VEC4:35666,LINEAR:9729,REPEAT:10497,SAMPLER_2D:35678,POINTS:0,LINES:1,LINE_LOOP:2,LINE_STRIP:3,TRIANGLES:4,TRIANGLE_STRIP:5,TRIANGLE_FAN:6,UNSIGNED_BYTE:5121,UNSIGNED_SHORT:5123},ia={5120:Int8Array,5121:Uint8Array,5122:Int16Array,5123:Uint16Array,5125:Uint32Array,5126:Float32Array},u0={9728:Wt,9729:Xt,9984:kc,9985:Kr,9986:nr,9987:qn},d0={33071:ni,33648:Rr,10497:Pi},Vu={SCALAR:1,VEC2:2,VEC3:3,VEC4:4,MAT2:4,MAT3:9,MAT4:16},pd={POSITION:"position",NORMAL:"normal",TANGENT:"tangent",TEXCOORD_0:"uv",TEXCOORD_1:"uv1",TEXCOORD_2:"uv2",TEXCOORD_3:"uv3",COLOR_0:"color",WEIGHTS_0:"skinWeight",JOINTS_0:"skinIndex"},Ls={scale:"scale",translation:"position",rotation:"quaternion",weights:"morphTargetInfluences"},Gy={CUBICSPLINE:void 0,LINEAR:Ws,STEP:Vs},Wu={OPAQUE:"OPAQUE",MASK:"MASK",BLEND:"BLEND"};function Vy(i){return i.DefaultMaterial===void 0&&(i.DefaultMaterial=new mt({color:16777215,emissive:0,metalness:1,roughness:1,transparent:!1,depthTest:!0,side:Ui})),i.DefaultMaterial}function ar(i,e,t){for(let n in t.extensions)i[n]===void 0&&(e.userData.gltfExtensions=e.userData.gltfExtensions||{},e.userData.gltfExtensions[n]=t.extensions[n])}function Gi(i,e){e.extras!==void 0&&(typeof e.extras=="object"?Object.assign(i.userData,e.extras):console.warn("THREE.GLTFLoader: Ignoring primitive type .extras, "+e.extras))}function Wy(i,e,t){let n=!1,s=!1,r=!1;for(let l=0,h=e.length;l<h;l++){let u=e[l];if(u.POSITION!==void 0&&(n=!0),u.NORMAL!==void 0&&(s=!0),u.COLOR_0!==void 0&&(r=!0),n&&s&&r)break}if(!n&&!s&&!r)return Promise.resolve(i);let a=[],o=[],c=[];for(let l=0,h=e.length;l<h;l++){let u=e[l];if(n){let d=u.POSITION!==void 0?t.getDependency("accessor",u.POSITION):i.attributes.position;a.push(d)}if(s){let d=u.NORMAL!==void 0?t.getDependency("accessor",u.NORMAL):i.attributes.normal;o.push(d)}if(r){let d=u.COLOR_0!==void 0?t.getDependency("accessor",u.COLOR_0):i.attributes.color;c.push(d)}}return Promise.all([Promise.all(a),Promise.all(o),Promise.all(c)]).then(function(l){let h=l[0],u=l[1],d=l[2];return n&&(i.morphAttributes.position=h),s&&(i.morphAttributes.normal=u),r&&(i.morphAttributes.color=d),i.morphTargetsRelative=!0,i})}function qy(i,e){if(i.updateMorphTargets(),e.weights!==void 0)for(let t=0,n=e.weights.length;t<n;t++)i.morphTargetInfluences[t]=e.weights[t];if(e.extras&&Array.isArray(e.extras.targetNames)){let t=e.extras.targetNames;if(i.morphTargetInfluences.length===t.length){i.morphTargetDictionary={};for(let n=0,s=t.length;n<s;n++)i.morphTargetDictionary[t[n]]=n}else console.warn("THREE.GLTFLoader: Invalid extras.targetNames length. Ignoring names.")}}function Xy(i){let e,t=i.extensions&&i.extensions[ut.KHR_DRACO_MESH_COMPRESSION];if(t?e="draco:"+t.bufferView+":"+t.indices+":"+qu(t.attributes):e=i.indices+":"+qu(i.attributes)+":"+i.mode,i.targets!==void 0)for(let n=0,s=i.targets.length;n<s;n++)e+=":"+qu(i.targets[n]);return e}function qu(i){let e="",t=Object.keys(i).sort();for(let n=0,s=t.length;n<s;n++)e+=t[n]+":"+i[t[n]]+";";return e}function md(i){switch(i){case Int8Array:return 1/127;case Uint8Array:return 1/255;case Int16Array:return 1/32767;case Uint16Array:return 1/65535;default:throw new Error("THREE.GLTFLoader: Unsupported normalized accessor component type.")}}function jy(i){return i.search(/\.jpe?g($|\?)/i)>0||i.search(/^data\:image\/jpeg/)===0?"image/jpeg":i.search(/\.webp($|\?)/i)>0||i.search(/^data\:image\/webp/)===0?"image/webp":i.search(/\.ktx2($|\?)/i)>0||i.search(/^data\:image\/ktx2/)===0?"image/ktx2":"image/png"}var Ky=new Ze,gd=class{constructor(e={},t={}){this.json=e,this.extensions={},this.plugins={},this.options=t,this.cache=new zy,this.associations=new Map,this.primitiveCache={},this.nodeCache={},this.meshCache={refs:{},uses:{}},this.cameraCache={refs:{},uses:{}},this.lightCache={refs:{},uses:{}},this.sourceCache={},this.textureCache={},this.nodeNamesUsed={};let n=!1,s=-1,r=!1,a=-1;if(typeof navigator<"u"&&typeof navigator.userAgent<"u"){let o=navigator.userAgent;n=/^((?!chrome|android).)*safari/i.test(o)===!0;let c=o.match(/Version\/(\d+)/);s=n&&c?parseInt(c[1],10):-1,r=o.indexOf("Firefox")>-1,a=r?o.match(/Firefox\/([0-9]+)\./)[1]:-1}typeof createImageBitmap>"u"||n&&s<17||r&&a<98?this.textureLoader=new Js(this.options.manager):this.textureLoader=new Za(this.options.manager),this.textureLoader.setCrossOrigin(this.options.crossOrigin),this.textureLoader.setRequestHeader(this.options.requestHeader),this.fileLoader=new Wr(this.options.manager),this.fileLoader.setResponseType("arraybuffer"),this.options.crossOrigin==="use-credentials"&&this.fileLoader.setWithCredentials(!0)}setExtensions(e){this.extensions=e}setPlugins(e){this.plugins=e}parse(e,t){let n=this,s=this.json,r=this.extensions;this.cache.removeAll(),this.nodeCache={},this._invokeAll(function(a){return a._markDefs&&a._markDefs()}),Promise.all(this._invokeAll(function(a){return a.beforeRoot&&a.beforeRoot()})).then(function(){return Promise.all([n.getDependencies("scene"),n.getDependencies("animation"),n.getDependencies("camera")])}).then(function(a){let o={scene:a[0][s.scene||0],scenes:a[0],animations:a[1],cameras:a[2],asset:s.asset,parser:n,userData:{}};return ar(r,o,s),Gi(o,s),Promise.all(n._invokeAll(function(c){return c.afterRoot&&c.afterRoot(o)})).then(function(){for(let c of o.scenes)c.updateMatrixWorld();e(o)})}).catch(t)}_markDefs(){let e=this.json.nodes||[],t=this.json.skins||[],n=this.json.meshes||[];for(let s=0,r=t.length;s<r;s++){let a=t[s].joints;for(let o=0,c=a.length;o<c;o++)e[a[o]].isBone=!0}for(let s=0,r=e.length;s<r;s++){let a=e[s];a.mesh!==void 0&&(this._addNodeRef(this.meshCache,a.mesh),a.skin!==void 0&&(n[a.mesh].isSkinnedMesh=!0)),a.camera!==void 0&&this._addNodeRef(this.cameraCache,a.camera)}}_addNodeRef(e,t){t!==void 0&&(e.refs[t]===void 0&&(e.refs[t]=e.uses[t]=0),e.refs[t]++)}_getNodeRef(e,t,n){if(e.refs[t]<=1)return n;let s=n.clone(),r=(a,o)=>{let c=this.associations.get(a);c!=null&&this.associations.set(o,c);for(let[l,h]of a.children.entries())r(h,o.children[l])};return r(n,s),s.name+="_instance_"+e.uses[t]++,s}_invokeOne(e){let t=Object.values(this.plugins);t.push(this);for(let n=0;n<t.length;n++){let s=e(t[n]);if(s)return s}return null}_invokeAll(e){let t=Object.values(this.plugins);t.unshift(this);let n=[];for(let s=0;s<t.length;s++){let r=e(t[s]);r&&n.push(r)}return n}getDependency(e,t){let n=e+":"+t,s=this.cache.get(n);if(!s){switch(e){case"scene":s=this.loadScene(t);break;case"node":s=this._invokeOne(function(r){return r.loadNode&&r.loadNode(t)});break;case"mesh":s=this._invokeOne(function(r){return r.loadMesh&&r.loadMesh(t)});break;case"accessor":s=this.loadAccessor(t);break;case"bufferView":s=this._invokeOne(function(r){return r.loadBufferView&&r.loadBufferView(t)});break;case"buffer":s=this.loadBuffer(t);break;case"material":s=this._invokeOne(function(r){return r.loadMaterial&&r.loadMaterial(t)});break;case"texture":s=this._invokeOne(function(r){return r.loadTexture&&r.loadTexture(t)});break;case"skin":s=this.loadSkin(t);break;case"animation":s=this._invokeOne(function(r){return r.loadAnimation&&r.loadAnimation(t)});break;case"camera":s=this.loadCamera(t);break;default:if(s=this._invokeOne(function(r){return r!=this&&r.getDependency&&r.getDependency(e,t)}),!s)throw new Error("Unknown type: "+e);break}this.cache.add(n,s)}return s}getDependencies(e){let t=this.cache.get(e);if(!t){let n=this,s=this.json[e+(e==="mesh"?"es":"s")]||[];t=Promise.all(s.map(function(r,a){return n.getDependency(e,a)})),this.cache.add(e,t)}return t}loadBuffer(e){let t=this.json.buffers[e],n=this.fileLoader;if(t.type&&t.type!=="arraybuffer")throw new Error("THREE.GLTFLoader: "+t.type+" buffer type is not supported.");if(t.uri===void 0&&e===0)return Promise.resolve(this.extensions[ut.KHR_BINARY_GLTF].body);let s=this.options;return new Promise(function(r,a){n.load(is.resolveURL(t.uri,s.path),r,void 0,function(){a(new Error('THREE.GLTFLoader: Failed to load buffer "'+t.uri+'".'))})})}loadBufferView(e){let t=this.json.bufferViews[e];return this.getDependency("buffer",t.buffer).then(function(n){let s=t.byteLength||0,r=t.byteOffset||0;return n.slice(r,r+s)})}loadAccessor(e){let t=this,n=this.json,s=this.json.accessors[e];if(s.bufferView===void 0&&s.sparse===void 0){let a=Vu[s.type],o=ia[s.componentType],c=s.normalized===!0,l=new o(s.count*a);return Promise.resolve(new an(l,a,c))}let r=[];return s.bufferView!==void 0?r.push(this.getDependency("bufferView",s.bufferView)):r.push(null),s.sparse!==void 0&&(r.push(this.getDependency("bufferView",s.sparse.indices.bufferView)),r.push(this.getDependency("bufferView",s.sparse.values.bufferView))),Promise.all(r).then(function(a){let o=a[0],c=Vu[s.type],l=ia[s.componentType],h=l.BYTES_PER_ELEMENT,u=h*c,d=s.byteOffset||0,f=s.bufferView!==void 0?n.bufferViews[s.bufferView].byteStride:void 0,m=s.normalized===!0,b,g;if(f&&f!==u){let p=Math.floor(d/f),_="InterleavedBuffer:"+s.bufferView+":"+s.componentType+":"+p+":"+s.count,S=t.cache.get(_);S||(b=new l(o,p*f,s.count*f/h),S=new Fr(b,f/h),t.cache.add(_,S)),g=new Ur(S,c,d%f/h,m)}else o===null?b=new l(s.count*c):b=new l(o,d,s.count*c),g=new an(b,c,m);if(s.sparse!==void 0){let p=Vu.SCALAR,_=ia[s.sparse.indices.componentType],S=s.sparse.indices.byteOffset||0,x=s.sparse.values.byteOffset||0,M=new _(a[1],S,s.sparse.count*p),E=new l(a[2],x,s.sparse.count*c);o!==null&&(g=new an(g.array.slice(),g.itemSize,g.normalized)),g.normalized=!1;for(let A=0,y=M.length;A<y;A++){let w=M[A];if(g.setX(w,E[A*c]),c>=2&&g.setY(w,E[A*c+1]),c>=3&&g.setZ(w,E[A*c+2]),c>=4&&g.setW(w,E[A*c+3]),c>=5)throw new Error("THREE.GLTFLoader: Unsupported itemSize in sparse BufferAttribute.")}g.normalized=m}return g})}loadTexture(e){let t=this.json,n=this.options,r=t.textures[e].source,a=t.images[r],o=this.textureLoader;if(a.uri){let c=n.manager.getHandler(a.uri);c!==null&&(o=c)}return this.loadTextureImage(e,r,o)}loadTextureImage(e,t,n){let s=this,r=this.json,a=r.textures[e],o=r.images[t],c=(o.uri||o.bufferView)+":"+a.sampler;if(this.textureCache[c])return this.textureCache[c];let l=this.loadImageSource(t,n).then(function(h){h.flipY=!1,h.name=a.name||o.name||"",h.name===""&&typeof o.uri=="string"&&o.uri.startsWith("data:image/")===!1&&(h.name=o.uri);let d=(r.samplers||{})[a.sampler]||{};return h.magFilter=u0[d.magFilter]||Xt,h.minFilter=u0[d.minFilter]||qn,h.wrapS=d0[d.wrapS]||Pi,h.wrapT=d0[d.wrapT]||Pi,h.generateMipmaps=!h.isCompressedTexture&&h.minFilter!==Wt&&h.minFilter!==Xt,s.associations.set(h,{textures:e}),h}).catch(function(){return null});return this.textureCache[c]=l,l}loadImageSource(e,t){let n=this,s=this.json,r=this.options;if(this.sourceCache[e]!==void 0)return this.sourceCache[e].then(u=>u.clone());let a=s.images[e],o=self.URL||self.webkitURL,c=a.uri||"",l=!1;if(a.bufferView!==void 0)c=n.getDependency("bufferView",a.bufferView).then(function(u){l=!0;let d=new Blob([u],{type:a.mimeType});return c=o.createObjectURL(d),c});else if(a.uri===void 0)throw new Error("THREE.GLTFLoader: Image "+e+" is missing URI and bufferView");let h=Promise.resolve(c).then(function(u){return new Promise(function(d,f){let m=d;t.isImageBitmapLoader===!0&&(m=function(b){let g=new hn(b);g.needsUpdate=!0,d(g)}),t.load(is.resolveURL(u,r.path),m,void 0,f)})}).then(function(u){return l===!0&&o.revokeObjectURL(c),Gi(u,a),u.userData.mimeType=a.mimeType||jy(a.uri),u}).catch(function(u){throw console.error("THREE.GLTFLoader: Couldn't load texture",c),u});return this.sourceCache[e]=h,h}assignTexture(e,t,n,s){let r=this;return this.getDependency("texture",n.index).then(function(a){if(!a)return null;if(n.texCoord!==void 0&&n.texCoord>0&&(a=a.clone(),a.channel=n.texCoord),r.extensions[ut.KHR_TEXTURE_TRANSFORM]){let o=n.extensions!==void 0?n.extensions[ut.KHR_TEXTURE_TRANSFORM]:void 0;if(o){let c=r.associations.get(a);a=r.extensions[ut.KHR_TEXTURE_TRANSFORM].extendTexture(a,o),r.associations.set(a,c)}}return s!==void 0&&(a.colorSpace=s),e[t]=a,a})}assignFinalMaterial(e){let t=e.geometry,n=e.material,s=t.attributes.tangent===void 0,r=t.attributes.color!==void 0,a=t.attributes.normal===void 0;if(e.isPoints){let o="PointsMaterial:"+n.uuid,c=this.cache.get(o);c||(c=new zr,Nn.prototype.copy.call(c,n),c.color.copy(n.color),c.map=n.map,c.sizeAttenuation=!1,this.cache.add(o,c)),n=c}else if(e.isLine){let o="LineBasicMaterial:"+n.uuid,c=this.cache.get(o);c||(c=new $i,Nn.prototype.copy.call(c,n),c.color.copy(n.color),c.map=n.map,this.cache.add(o,c)),n=c}if(s||r||a){let o="ClonedMaterial:"+n.uuid+":";s&&(o+="derivative-tangents:"),r&&(o+="vertex-colors:"),a&&(o+="flat-shading:");let c=this.cache.get(o);c||(c=n.clone(),r&&(c.vertexColors=!0),a&&(c.flatShading=!0),s&&(c.normalScale&&(c.normalScale.y*=-1),c.clearcoatNormalScale&&(c.clearcoatNormalScale.y*=-1)),this.cache.add(o,c),this.associations.set(c,this.associations.get(n))),n=c}e.material=n}getMaterialType(){return mt}loadMaterial(e){let t=this,n=this.json,s=this.extensions,r=n.materials[e],a,o={},c=r.extensions||{},l=[];if(c[ut.KHR_MATERIALS_UNLIT]){let u=s[ut.KHR_MATERIALS_UNLIT];a=u.getMaterialType(),l.push(u.extendParams(o,r,t))}else{let u=r.pbrMetallicRoughness||{};if(o.color=new Be(1,1,1),o.opacity=1,Array.isArray(u.baseColorFactor)){let d=u.baseColorFactor;o.color.setRGB(d[0],d[1],d[2],Dn),o.opacity=d[3]}u.baseColorTexture!==void 0&&l.push(t.assignTexture(o,"map",u.baseColorTexture,pt)),o.metalness=u.metallicFactor!==void 0?u.metallicFactor:1,o.roughness=u.roughnessFactor!==void 0?u.roughnessFactor:1,u.metallicRoughnessTexture!==void 0&&(l.push(t.assignTexture(o,"metalnessMap",u.metallicRoughnessTexture)),l.push(t.assignTexture(o,"roughnessMap",u.metallicRoughnessTexture))),a=this._invokeOne(function(d){return d.getMaterialType&&d.getMaterialType(e)}),l.push(Promise.all(this._invokeAll(function(d){return d.extendMaterialParams&&d.extendMaterialParams(e,o)})))}r.doubleSided===!0&&(o.side=oi);let h=r.alphaMode||Wu.OPAQUE;if(h===Wu.BLEND?(o.transparent=!0,o.depthWrite=!1):(o.transparent=!1,h===Wu.MASK&&(o.alphaTest=r.alphaCutoff!==void 0?r.alphaCutoff:.5)),r.normalTexture!==void 0&&a!==jt&&(l.push(t.assignTexture(o,"normalMap",r.normalTexture)),o.normalScale=new ae(1,1),r.normalTexture.scale!==void 0)){let u=r.normalTexture.scale;o.normalScale.set(u,u)}if(r.occlusionTexture!==void 0&&a!==jt&&(l.push(t.assignTexture(o,"aoMap",r.occlusionTexture)),r.occlusionTexture.strength!==void 0&&(o.aoMapIntensity=r.occlusionTexture.strength)),r.emissiveFactor!==void 0&&a!==jt){let u=r.emissiveFactor;o.emissive=new Be().setRGB(u[0],u[1],u[2],Dn)}return r.emissiveTexture!==void 0&&a!==jt&&l.push(t.assignTexture(o,"emissiveMap",r.emissiveTexture,pt)),Promise.all(l).then(function(){let u=new a(o);return r.name&&(u.name=r.name),Gi(u,r),t.associations.set(u,{materials:e}),r.extensions&&ar(s,u,r),u})}createUniqueName(e){let t=Ot.sanitizeNodeName(e||"");return t in this.nodeNamesUsed?t+"_"+ ++this.nodeNamesUsed[t]:(this.nodeNamesUsed[t]=0,t)}loadGeometries(e){let t=this,n=this.extensions,s=this.primitiveCache;function r(o){return n[ut.KHR_DRACO_MESH_COMPRESSION].decodePrimitive(o,t).then(function(c){return f0(c,o,t)})}let a=[];for(let o=0,c=e.length;o<c;o++){let l=e[o],h=Xy(l),u=s[h];if(u)a.push(u.promise);else{let d;l.extensions&&l.extensions[ut.KHR_DRACO_MESH_COMPRESSION]?d=r(l):d=f0(new Bt,l,t),l.mode===li.TRIANGLE_STRIP?d=d.then(f=>Nu(f,co)):l.mode===li.TRIANGLE_FAN&&(d=d.then(f=>Nu(f,Zr))),s[h]={primitive:l,promise:d},a.push(d)}}return Promise.all(a)}loadMesh(e){let t=this,n=this.json,s=this.extensions,r=n.meshes[e],a=r.primitives,o=[];for(let c=0,l=a.length;c<l;c++){let h=a[c].material===void 0?Vy(this.cache):this.getDependency("material",a[c].material);o.push(h)}return o.push(t.loadGeometries(a)),Promise.all(o).then(async function(c){let l=c.slice(0,c.length-1),h=c[c.length-1],u=[];for(let f=0,m=h.length;f<m;f++){let b=h[f],g=a[f],p,_=l[f];if(g.mode===li.TRIANGLES||g.mode===li.TRIANGLE_STRIP||g.mode===li.TRIANGLE_FAN||g.mode===void 0){let S=r.isSkinnedMesh===!0,x=b.hasAttribute("skinIndex")&&b.hasAttribute("skinWeight");S&&x===!1&&console.warn("THREE.GLTFLoader: Missing skinIndex or skinWeight attributes. Skinning disabled."),p=S&&x?new Ca(b,_):new ke(b,_),p.isSkinnedMesh===!0&&p.normalizeSkinWeights()}else if(g.mode===li.LINES)p=new Ia(b,_);else if(g.mode===li.LINE_STRIP)p=new js(b,_);else if(g.mode===li.LINE_LOOP)p=new La(b,_);else if(g.mode===li.POINTS)p=new Da(b,_);else throw new Error("THREE.GLTFLoader: Primitive mode unsupported: "+g.mode);Object.keys(p.geometry.morphAttributes).length>0&&qy(p,r),p.name=t.createUniqueName(r.name||"mesh_"+e),Gi(p,r),g.extensions&&ar(s,p,g),t.assignFinalMaterial(p),u.push(p)}for(let f=0,m=u.length;f<m;f++)t.associations.set(u[f],{meshes:e,primitives:f});if(u.length===1)return r.extensions&&ar(s,u[0],r),u[0];let d=new at;r.extensions&&ar(s,d,r),t.associations.set(d,{meshes:e});for(let f=0,m=u.length;f<m;f++)d.add(u[f]);return d})}loadCamera(e){let t,n=this.json.cameras[e],s=n[n.type];if(!s){console.warn("THREE.GLTFLoader: Missing camera parameters.");return}return n.type==="perspective"?t=new Yt(Ht.radToDeg(s.yfov),s.aspectRatio||1,s.znear||1,s.zfar||2e6):n.type==="orthographic"&&(t=new Es(-s.xmag,s.xmag,s.ymag,-s.ymag,s.znear,s.zfar)),n.name&&(t.name=this.createUniqueName(n.name)),Gi(t,n),Promise.resolve(t)}loadSkin(e){let t=this.json.skins[e],n=[];for(let s=0,r=t.joints.length;s<r;s++)n.push(this._loadNodeShallow(t.joints[s]));return t.inverseBindMatrices!==void 0?n.push(this.getDependency("accessor",t.inverseBindMatrices)):n.push(null),Promise.all(n).then(function(s){let r=s.pop(),a=s,o=[],c=[];for(let l=0,h=a.length;l<h;l++){let u=a[l];if(u){o.push(u);let d=new Ze;r!==null&&d.fromArray(r.array,l*16),c.push(d)}else console.warn('THREE.GLTFLoader: Joint "%s" could not be found.',t.joints[l])}return new Pa(o,c)})}loadAnimation(e){let t=this.json,n=this,s=t.animations[e],r=s.name?s.name:"animation_"+e,a=[],o=[],c=[],l=[],h=[];for(let u=0,d=s.channels.length;u<d;u++){let f=s.channels[u],m=s.samplers[f.sampler],b=f.target,g=b.node,p=s.parameters!==void 0?s.parameters[m.input]:m.input,_=s.parameters!==void 0?s.parameters[m.output]:m.output;b.node!==void 0&&(a.push(this.getDependency("node",g)),o.push(this.getDependency("accessor",p)),c.push(this.getDependency("accessor",_)),l.push(m),h.push(b))}return Promise.all([Promise.all(a),Promise.all(o),Promise.all(c),Promise.all(l),Promise.all(h)]).then(function(u){let d=u[0],f=u[1],m=u[2],b=u[3],g=u[4],p=[];for(let S=0,x=d.length;S<x;S++){let M=d[S],E=f[S],A=m[S],y=b[S],w=g[S];if(M===void 0)continue;M.updateMatrix&&M.updateMatrix();let R=n._createAnimationTracks(M,E,A,y,w);if(R)for(let I=0;I<R.length;I++)p.push(R[I])}let _=new Ka(r,void 0,p);return Gi(_,s),_})}createNodeMesh(e){let t=this.json,n=this,s=t.nodes[e];return s.mesh===void 0?null:n.getDependency("mesh",s.mesh).then(function(r){let a=n._getNodeRef(n.meshCache,s.mesh,r);return s.weights!==void 0&&a.traverse(function(o){if(o.isMesh)for(let c=0,l=s.weights.length;c<l;c++)o.morphTargetInfluences[c]=s.weights[c]}),a})}loadNode(e){let t=this.json,n=this,s=t.nodes[e],r=n._loadNodeShallow(e),a=[],o=s.children||[];for(let l=0,h=o.length;l<h;l++)a.push(n.getDependency("node",o[l]));let c=s.skin===void 0?Promise.resolve(null):n.getDependency("skin",s.skin);return Promise.all([r,Promise.all(a),c]).then(function(l){let h=l[0],u=l[1],d=l[2];d!==null&&h.traverse(function(f){f.isSkinnedMesh&&f.bind(d,Ky)});for(let f=0,m=u.length;f<m;f++)h.add(u[f]);if(h.userData.pivot!==void 0&&u.length>0){let f=h.userData.pivot,m=u[0];h.pivot=new P().fromArray(f),h.position.x-=f[0],h.position.y-=f[1],h.position.z-=f[2],m.position.set(0,0,0),delete h.userData.pivot}return h})}_loadNodeShallow(e){let t=this.json,n=this.extensions,s=this;if(this.nodeCache[e]!==void 0)return this.nodeCache[e];let r=t.nodes[e],a=r.name?s.createUniqueName(r.name):"",o=[],c=s._invokeOne(function(l){return l.createNodeMesh&&l.createNodeMesh(e)});return c&&o.push(c),r.camera!==void 0&&o.push(s.getDependency("camera",r.camera).then(function(l){return s._getNodeRef(s.cameraCache,r.camera,l)})),s._invokeAll(function(l){return l.createNodeAttachment&&l.createNodeAttachment(e)}).forEach(function(l){o.push(l)}),this.nodeCache[e]=Promise.all(o).then(function(l){let h;if(r.isBone===!0?h=new Or:l.length>1?h=new at:l.length===1?h=l[0]:h=new At,h!==l[0])for(let u=0,d=l.length;u<d;u++)h.add(l[u]);if(r.name&&(h.userData.name=r.name,h.name=a),Gi(h,r),r.extensions&&ar(n,h,r),r.matrix!==void 0){let u=new Ze;u.fromArray(r.matrix),h.applyMatrix4(u)}else r.translation!==void 0&&h.position.fromArray(r.translation),r.rotation!==void 0&&h.quaternion.fromArray(r.rotation),r.scale!==void 0&&h.scale.fromArray(r.scale);if(!s.associations.has(h))s.associations.set(h,{});else if(r.mesh!==void 0&&s.meshCache.refs[r.mesh]>1){let u=s.associations.get(h);s.associations.set(h,{...u})}return s.associations.get(h).nodes=e,h}),this.nodeCache[e]}loadScene(e){let t=this.extensions,n=this.json.scenes[e],s=this,r=new at;n.name&&(r.name=s.createUniqueName(n.name)),Gi(r,n),n.extensions&&ar(t,r,n);let a=n.nodes||[],o=[];for(let c=0,l=a.length;c<l;c++)o.push(s.getDependency("node",a[c]));return Promise.all(o).then(function(c){for(let h=0,u=c.length;h<u;h++){let d=c[h];d.parent!==null?r.add(c0(d)):r.add(d)}let l=h=>{let u=new Map;for(let[d,f]of s.associations)(d instanceof Nn||d instanceof hn)&&u.set(d,f);return h.traverse(d=>{let f=s.associations.get(d);f!=null&&u.set(d,f)}),u};return s.associations=l(r),r})}_createAnimationTracks(e,t,n,s,r){let a=[],o=e.name?e.name:e.uuid,c=[];function l(f){f.morphTargetInfluences&&c.push(f.name?f.name:f.uuid)}Ls[r.path]===Ls.weights?(l(e),e.isGroup&&e.children.forEach(l)):c.push(o);let h;switch(Ls[r.path]){case Ls.weights:h=es;break;case Ls.rotation:h=ts;break;case Ls.translation:case Ls.scale:h=Ss;break;default:n.itemSize===1?h=es:h=Ss;break}let u=s.interpolation!==void 0?Gy[s.interpolation]:Ws,d=this._getArrayFromAccessor(n);for(let f=0,m=c.length;f<m;f++){let b=new h(c[f]+"."+Ls[r.path],t.array,d,u);s.interpolation==="CUBICSPLINE"&&this._createCubicSplineTrackInterpolant(b),a.push(b)}return a}_getArrayFromAccessor(e){let t=e.array;if(e.normalized){let n=md(t.constructor),s=new Float32Array(t.length);for(let r=0,a=t.length;r<a;r++)s[r]=t[r]*n;t=s}return t}_createCubicSplineTrackInterpolant(e){e.createInterpolant=function(n){let s=this instanceof ts?fd:Hl;return new s(this.times,this.values,this.getValueSize()/3,n)},e.createInterpolant.isInterpolantFactoryMethodGLTFCubicSpline=!0}};function Yy(i,e,t){let n=e.attributes,s=new en;if(n.POSITION!==void 0){let o=t.json.accessors[n.POSITION],c=o.min,l=o.max;if(c!==void 0&&l!==void 0){if(s.set(new P(c[0],c[1],c[2]),new P(l[0],l[1],l[2])),o.normalized){let h=md(ia[o.componentType]);s.min.multiplyScalar(h),s.max.multiplyScalar(h)}}else{console.warn("THREE.GLTFLoader: Missing min/max properties for accessor POSITION.");return}}else return;let r=e.targets;if(r!==void 0){let o=new P,c=new P;for(let l=0,h=r.length;l<h;l++){let u=r[l];if(u.POSITION!==void 0){let d=t.json.accessors[u.POSITION],f=d.min,m=d.max;if(f!==void 0&&m!==void 0){if(c.setX(Math.max(Math.abs(f[0]),Math.abs(m[0]))),c.setY(Math.max(Math.abs(f[1]),Math.abs(m[1]))),c.setZ(Math.max(Math.abs(f[2]),Math.abs(m[2]))),d.normalized){let b=md(ia[d.componentType]);c.multiplyScalar(b)}o.max(c)}else console.warn("THREE.GLTFLoader: Missing min/max properties for accessor POSITION.")}}s.expandByVector(o)}i.boundingBox=s;let a=new mn;s.getCenter(a.center),a.radius=s.min.distanceTo(s.max)/2,i.boundingSphere=a}function f0(i,e,t){let n=e.attributes,s=[];function r(a,o){return t.getDependency("accessor",a).then(function(c){i.setAttribute(o,c)})}for(let a in n){let o=pd[a]||a.toLowerCase();o in i.attributes||s.push(r(n[a],o))}if(e.indices!==void 0&&!i.index){let a=t.getDependency("accessor",e.indices).then(function(o){i.setIndex(o)});s.push(a)}return lt.workingColorSpace!==Dn&&"COLOR_0"in n&&console.warn(`THREE.GLTFLoader: Converting vertex colors from "srgb-linear" to "${lt.workingColorSpace}" not supported.`),Gi(i,e),Yy(i,e,t),Promise.all(s).then(function(){return e.targets!==void 0?Wy(i,e.targets,t):i})}var m0=(function(){var i="b9H79Tebbbe8Fv9Gbb9Gvuuuuueu9Giuuub9Geueu9Giuuueuixkbeeeddddillviebeoweuecj:Gdkr;Neqo9TW9T9VV95dbH9F9F939H79T9F9J9H229F9Jt9VV7bb8A9TW79O9V9Wt9F9KW9J9V9KW9wWVtW949c919M9MWVbeY9TW79O9V9Wt9F9KW9J9V9KW69U9KW949c919M9MWVbdE9TW79O9V9Wt9F9KW9J9V9KW69U9KW949tWG91W9U9JWbiL9TW79O9V9Wt9F9KW9J9V9KWS9P2tWV9p9JtblK9TW79O9V9Wt9F9KW9J9V9KWS9P2tWV9r919HtbvL9TW79O9V9Wt9F9KW9J9V9KWS9P2tWVT949WboY9TW79O9V9Wt9F9KW9J9V9KWS9P2tWVJ9V29VVbrl79IV9Rbwq:VZkdbk:XYi5ud9:du8Jjjjjbcj;kb9Rgv8Kjjjjbc9:hodnalTmbcuhoaiRbbgrc;WeGc:Ge9hmbarcsGgwce0mbc9:hoalcufadcd4cbawEgDadfgrcKcaawEgqaraq0Egk6mbaicefhxcj;abad9Uc;WFbGcjdadca0EhmaialfgPar9Rgoadfhsavaoadz:jjjjbgzceVhHcbhOdndninaeaO9nmeaPax9RaD6mdamaeaO9RaOamfgoae6EgAcsfglc9WGhCabaOad2fhXaAcethQaxaDfhiaOaeaoaeao6E9RhLalcl4cifcd4hKazcj;cbfaAfhYcbh8AazcjdfhEaHh3incbh5dnawTmbaxa8Acd4fRbbh5kcbh8Eazcj;cbfhqinaih8Fdndndndna5a8Ecet4ciGgoc9:fPdebdkaPa8F9RaA6mrazcj;cbfa8EaA2fa8FaAz:jjjjb8Aa8FaAfhixdkazcj;cbfa8EaA2fcbaAz:kjjjb8Aa8FhixekaPa8F9RaK6mva8FaKfhidnaCTmbaPai9RcK6mbaocdtc:q:G:cjbfcj:G:cjbawEhaczhrcbhlinargoc9Wfghaqfhrdndndndndndnaaa8Fahco4fRbbalcoG4ciGcdtfydbPDbedvivvvlvkar9cb83bwar9cb83bbxlkarcbaiRbdai8Xbb9c:c:qj:bw9:9c:q;c1:I1e:d9c:b:c:e1z9:gg9cjjjjjz:dg8J9qE86bbaqaofgrcGfcbaicdfa8J9c8N1:NfghRbbag9cjjjjjw:dg8J9qE86bbarcVfcbaha8J9c8M1:NfghRbbag9cjjjjjl:dg8J9qE86bbarc7fcbaha8J9c8L1:NfghRbbag9cjjjjjd:dg8J9qE86bbarctfcbaha8J9c8K1:NfghRbbag9cjjjjje:dg8J9qE86bbarc91fcbaha8J9c8J1:NfghRbbag9cjjjj;ab:dg8J9qE86bbarc4fcbaha8J9cg1:NfghRbbag9cjjjja:dg8J9qE86bbarc93fcbaha8J9ch1:NfghRbbag9cjjjjz:dgg9qE86bbarc94fcbahag9ca1:NfghRbbai8Xbe9c:c:qj:bw9:9c:q;c1:I1e:d9c:b:c:e1z9:gg9cjjjjjz:dg8J9qE86bbarc95fcbaha8J9c8N1:NfgiRbbag9cjjjjjw:dg8J9qE86bbarc96fcbaia8J9c8M1:NfgiRbbag9cjjjjjl:dg8J9qE86bbarc97fcbaia8J9c8L1:NfgiRbbag9cjjjjjd:dg8J9qE86bbarc98fcbaia8J9c8K1:NfgiRbbag9cjjjjje:dg8J9qE86bbarc99fcbaia8J9c8J1:NfgiRbbag9cjjjj;ab:dg8J9qE86bbarc9:fcbaia8J9cg1:NfgiRbbag9cjjjja:dg8J9qE86bbarcufcbaia8J9ch1:NfgiRbbag9cjjjjz:dgg9qE86bbaiag9ca1:NfhixikaraiRblaiRbbghco4g8Ka8KciSg8KE86bbaqaofgrcGfaiclfa8Kfg8KRbbahcl4ciGg8La8LciSg8LE86bbarcVfa8Ka8Lfg8KRbbahcd4ciGg8La8LciSg8LE86bbarc7fa8Ka8Lfg8KRbbahciGghahciSghE86bbarctfa8Kahfg8KRbbaiRbeghco4g8La8LciSg8LE86bbarc91fa8Ka8Lfg8KRbbahcl4ciGg8La8LciSg8LE86bbarc4fa8Ka8Lfg8KRbbahcd4ciGg8La8LciSg8LE86bbarc93fa8Ka8Lfg8KRbbahciGghahciSghE86bbarc94fa8Kahfg8KRbbaiRbdghco4g8La8LciSg8LE86bbarc95fa8Ka8Lfg8KRbbahcl4ciGg8La8LciSg8LE86bbarc96fa8Ka8Lfg8KRbbahcd4ciGg8La8LciSg8LE86bbarc97fa8Ka8Lfg8KRbbahciGghahciSghE86bbarc98fa8KahfghRbbaiRbigico4g8Ka8KciSg8KE86bbarc99faha8KfghRbbaicl4ciGg8Ka8KciSg8KE86bbarc9:faha8KfghRbbaicd4ciGg8Ka8KciSg8KE86bbarcufaha8KfgrRbbaiciGgiaiciSgiE86bbaraifhixdkaraiRbwaiRbbghcl4g8Ka8KcsSg8KE86bbaqaofgrcGfaicwfa8Kfg8KRbbahcsGghahcsSghE86bbarcVfa8KahfghRbbaiRbeg8Kcl4g8La8LcsSg8LE86bbarc7faha8LfghRbba8KcsGg8Ka8KcsSg8KE86bbarctfaha8KfghRbbaiRbdg8Kcl4g8La8LcsSg8LE86bbarc91faha8LfghRbba8KcsGg8Ka8KcsSg8KE86bbarc4faha8KfghRbbaiRbig8Kcl4g8La8LcsSg8LE86bbarc93faha8LfghRbba8KcsGg8Ka8KcsSg8KE86bbarc94faha8KfghRbbaiRblg8Kcl4g8La8LcsSg8LE86bbarc95faha8LfghRbba8KcsGg8Ka8KcsSg8KE86bbarc96faha8KfghRbbaiRbvg8Kcl4g8La8LcsSg8LE86bbarc97faha8LfghRbba8KcsGg8Ka8KcsSg8KE86bbarc98faha8KfghRbbaiRbog8Kcl4g8La8LcsSg8LE86bbarc99faha8LfghRbba8KcsGg8Ka8KcsSg8KE86bbarc9:faha8KfghRbbaiRbrgicl4g8Ka8KcsSg8KE86bbarcufaha8KfgrRbbaicsGgiaicsSgiE86bbaraifhixekarai8Pbw83bwarai8Pbb83bbaiczfhikdnaoaC9pmbalcdfhlaoczfhraPai9RcL0mekkaoaC6moaimexokaCmva8FTmvkaqaAfhqa8Ecefg8Ecl9hmbkdndndndnawTmbasa8Acd4fRbbgociGPlbedrbkaATmdaza8Afh8Fazcj;cbfhhcbh8EaEhaina8FRbbhraahocbhlinaoahalfRbbgqce4cbaqceG9R7arfgr86bbaoadfhoaAalcefgl9hmbkaacefhaa8Fcefh8FahaAfhha8Ecefg8Ecl9hmbxikkaATmeaza8Afhaazcj;cbfhhcbhoceh8EaYh8FinaEaofhlaa8Vbbhrcbhoinala8FaofRbbcwtahaofRbbgqVc;:FiGce4cbaqceG9R7arfgr87bbaladfhlaLaocefgofmbka8FaQfh8FcdhoaacdfhaahaQfhha8EceGhlcbh8EalmbxdkkaATmbaocl4h8Eaza8AfRbbhqcwhoa3hlinalRbbaotaqVhqalcefhlaocwfgoca9hmbkcbhhaEh8FaYhainazcj;cbfahfRbbhrcwhoaahlinalRbbaotarVhralaAfhlaocwfgoca9hmbkara8E94aq7hqcbhoa8Fhlinalaqao486bbalcefhlaocwfgoca9hmbka8Fadfh8FaacefhaahcefghaA9hmbkkaEclfhEa3clfh3a8Aclfg8Aad6mbkaXazcjdfaAad2z:jjjjb8AazazcjdfaAcufad2fadz:jjjjb8AaAaOfhOaihxaimbkc9:hoxdkcbc99aPax9RakSEhoxekc9:hokavcj;kbf8Kjjjjbaok:ysezu8Jjjjjbc;ae9Rgv8Kjjjjbc9:hodnalaeci9UgrcHf6mbcuhoaiRbbgwc;WeGc;Ge9hmbawcsGgDce0mbavc;abfcFecjez:kjjjb8Aav9cu83iUav9cu83i8Wav9cu83iyav9cu83iaav9cu83iKav9cu83izav9cu83iwav9cu83ibaialfc9WfhqaicefgwarfhldnaeTmbcmcsaDceSEhkcbhxcbhmcbhrcbhicbhoindnalaq9nmbc9:hoxikdndnawRbbgDc;Ve0mbavc;abfaoaDcu7gPcl4fcsGcitfgsydlhzasydbhHdndnaDcsGgsak9pmbavaiaPfcsGcdtfydbaxasEhDaxasTgOfhxxekdndnascsSmbcehOasc987asamffcefhDxekalcefhDal8SbbgscFeGhPdndnascu9mmbaDhlxekalcvfhlaPcFbGhPcrhsdninaD8SbbgOcFbGastaPVhPaOcu9kmeaDcefhDascrfgsc8J9hmbxdkkaDcefhlkcehOaPce4cbaPceG9R7amfhDkaDhmkavc;abfaocitfgsaDBdbasazBdlavaicdtfaDBdbavc;abfaocefcsGcitfgsaHBdbasaDBdlaocdfhoaOaifhidnadcd9hmbabarcetfgsaH87ebasclfaD87ebascdfaz87ebxdkabarcdtfgsaHBdbascwfaDBdbasclfazBdbxekdnaDcpe0mbavaiaqaDcsGfRbbgscl4gP9RcsGcdtfydbaxcefgOaPEhDavaias9RcsGcdtfydbaOaPTgzfgOascsGgPEhsaPThPdndnadcd9hmbabarcetfgHax87ebaHclfas87ebaHcdfaD87ebxekabarcdtfgHaxBdbaHcwfasBdbaHclfaDBdbkavaicdtfaxBdbavc;abfaocitfgHaDBdbaHaxBdlavaicefgicsGcdtfaDBdbavc;abfaocefcsGcitfgHasBdbaHaDBdlavaiazfgicsGcdtfasBdbavc;abfaocdfcsGcitfgDaxBdbaDasBdlaocifhoaiaPfhiaOaPfhxxekaxcbalRbbgsEgHaDc;:eSgDfhOascsGhAdndnascl4gCmbaOcefhzxekaOhzavaiaC9RcsGcdtfydbhOkdndnaAmbazcefhxxekazhxavaias9RcsGcdtfydbhzkdndnaDTmbalcefhDxekalcdfhDal8SbegPcFeGhsdnaPcu9kmbalcofhHascFbGhscrhldninaD8SbbgPcFbGaltasVhsaPcu9kmeaDcefhDalcrfglc8J9hmbkaHhDxekaDcefhDkasce4cbasceG9R7amfgmhHkdndnaCcsSmbaDhsxekaDcefhsaD8SbbglcFeGhPdnalcu9kmbaDcvfhOaPcFbGhPcrhldninas8SbbgDcFbGaltaPVhPaDcu9kmeascefhsalcrfglc8J9hmbkaOhsxekascefhskaPce4cbaPceG9R7amfgmhOkdndnaAcsSmbashlxekascefhlas8SbbgDcFeGhPdnaDcu9kmbascvfhzaPcFbGhPcrhDdninal8SbbgscFbGaDtaPVhPascu9kmealcefhlaDcrfgDc8J9hmbkazhlxekalcefhlkaPce4cbaPceG9R7amfgmhzkdndnadcd9hmbabarcetfgDaH87ebaDclfaz87ebaDcdfaO87ebxekabarcdtfgDaHBdbaDcwfazBdbaDclfaOBdbkavc;abfaocitfgDaOBdbaDaHBdlavaicdtfaHBdbavc;abfaocefcsGcitfgDazBdbaDaOBdlavaicefgicsGcdtfaOBdbavc;abfaocdfcsGcitfgDaHBdbaDazBdlavaiaCTaCcsSVfgicsGcdtfazBdbaiaATaAcsSVfhiaocifhokawcefhwaocsGhoaicsGhiarcifgrae6mbkkcbc99alaqSEhokavc;aef8Kjjjjbaok:clevu8Jjjjjbcz9Rhvdnalaecvf9pmbc9:skdnaiRbbc;:eGc;qeSmbcuskav9cb83iwaicefhoaialfc98fhrdnaeTmbdnadcdSmbcbhwindnaoar6mbc9:skaocefhlao8SbbgicFeGhddndnaicu9mmbalhoxekaocvfhoadcFbGhdcrhidninal8SbbgDcFbGaitadVhdaDcu9kmealcefhlaicrfgic8J9hmbxdkkalcefhokabawcdtfadc8Etc8F91adcd47avcwfadceGcdtVglydbfgiBdbalaiBdbawcefgwae9hmbxdkkcbhwindnaoar6mbc9:skaocefhlao8SbbgicFeGhddndnaicu9mmbalhoxekaocvfhoadcFbGhdcrhidninal8SbbgDcFbGaitadVhdaDcu9kmealcefhlaicrfgic8J9hmbxdkkalcefhokabawcetfadc8Etc8F91adcd47avcwfadceGcdtVglydbfgi87ebalaiBdbawcefgwae9hmbkkcbc99aoarSEk:Lvoeue99dud99eud99dndnadcl9hmbaeTmeindndnabcdfgd8Sbb:Yab8Sbbgi:Ygl:l:tabcefgv8Sbbgo:Ygr:l:tgwJbb;:9cawawNJbbbbawawJbbbb9GgDEgq:mgkaqaicb9iEalMgwawNakaqaocb9iEarMgqaqNMM:r:vglNJbbbZJbbb:;aDEMgr:lJbbb9p9DTmbar:Ohixekcjjjj94hikadai86bbdndnaqalNJbbbZJbbb:;aqJbbbb9GEMgq:lJbbb9p9DTmbaq:Ohdxekcjjjj94hdkavad86bbdndnawalNJbbbZJbbb:;awJbbbb9GEMgw:lJbbb9p9DTmbaw:Ohdxekcjjjj94hdkabad86bbabclfhbaecufgembxdkkaeTmbindndnabclfgd8Ueb:Yab8Uebgi:Ygl:l:tabcdfgv8Uebgo:Ygr:l:tgwJb;:FSawawNJbbbbawawJbbbb9GgDEgq:mgkaqaicb9iEalMgwawNakaqaocb9iEarMgqaqNMM:r:vglNJbbbZJbbb:;aDEMgr:lJbbb9p9DTmbar:Ohixekcjjjj94hikadai87ebdndnaqalNJbbbZJbbb:;aqJbbbb9GEMgq:lJbbb9p9DTmbaq:Ohdxekcjjjj94hdkavad87ebdndnawalNJbbbZJbbb:;awJbbbb9GEMgw:lJbbb9p9DTmbaw:Ohdxekcjjjj94hdkabad87ebabcwfhbaecufgembkkk:4ioiue99dud99dud99dnaeTmbcbhiabhlindndnal8Uebgv:YgoJ:ji:1Salcof8UebgrciVgw:Y:vgDNJbbbZJbbb:;avcu9kEMgq:lJbbb9p9DTmbaq:Ohkxekcjjjj94hkkalclf8Uebhvalcdf8UebhxalarcefciGcetfak87ebdndnax:YgqaDNJbbbZJbbb:;axcu9kEMgm:lJbbb9p9DTmbam:Ohxxekcjjjj94hxkabaiarciGgkfcd7cetfax87ebdndnav:YgmaDNJbbbZJbbb:;avcu9kEMgP:lJbbb9p9DTmbaP:Ohvxekcjjjj94hvkalarcufciGcetfav87ebdndnawaw2:ZgPaPMaoaoN:taqaqN:tamamN:tgoJbbbbaoJbbbb9GE:raDNJbbbZMgD:lJbbb9p9DTmbaD:Ohrxekcjjjj94hrkalakcetfar87ebalcwfhlaiclfhiaecufgembkkk9mbdnadcd4ae2gdTmbinababydbgecwtcw91:Yaece91cjjj98Gcjjj;8if::NUdbabclfhbadcufgdmbkkk:Tvirud99eudndnadcl9hmbaeTmeindndnabRbbgiabcefgl8Sbbgvabcdfgo8Sbbgrf9R:YJbbuJabcifgwRbbgdce4adVgDcd4aDVgDcl4aDVgD:Z:vgqNJbbbZMgk:lJbbb9p9DTmbak:Ohxxekcjjjj94hxkaoax86bbdndnaraif:YaqNJbbbZMgk:lJbbb9p9DTmbak:Ohoxekcjjjj94hokalao86bbdndnavaifar9R:YaqNJbbbZMgk:lJbbb9p9DTmbak:Ohixekcjjjj94hikabai86bbdndnaDadcetGadceGV:ZaqNJbbbZMgq:lJbbb9p9DTmbaq:Ohdxekcjjjj94hdkawad86bbabclfhbaecufgembxdkkaeTmbindndnab8Vebgiabcdfgl8Uebgvabclfgo8Uebgrf9R:YJbFu9habcofgw8Vebgdce4adVgDcd4aDVgDcl4aDVgDcw4aDVgD:Z:vgqNJbbbZMgk:lJbbb9p9DTmbak:Ohxxekcjjjj94hxkaoax87ebdndnaraif:YaqNJbbbZMgk:lJbbb9p9DTmbak:Ohoxekcjjjj94hokalao87ebdndnavaifar9R:YaqNJbbbZMgk:lJbbb9p9DTmbak:Ohixekcjjjj94hikabai87ebdndnaDadcetGadceGV:ZaqNJbbbZMgq:lJbbb9p9DTmbaq:Ohdxekcjjjj94hdkawad87ebabcwfhbaecufgembkkk9teiucbcbyd:K:G:cjbgeabcifc98GfgbBd:K:G:cjbdndnabZbcztgd9nmbcuhiabad9RcFFifcz4nbcuSmekaehikaik;LeeeudndnaeabVciGTmbabhixekdndnadcz9pmbabhixekabhiinaiaeydbBdbaiclfaeclfydbBdbaicwfaecwfydbBdbaicxfaecxfydbBdbaeczfheaiczfhiadc9Wfgdcs0mbkkadcl6mbinaiaeydbBdbaeclfheaiclfhiadc98fgdci0mbkkdnadTmbinaiaeRbb86bbaicefhiaecefheadcufgdmbkkabk;aeedudndnabciGTmbabhixekaecFeGc:b:c:ew2hldndnadcz9pmbabhixekabhiinaialBdbaicxfalBdbaicwfalBdbaiclfalBdbaiczfhiadc9Wfgdcs0mbkkadcl6mbinaialBdbaiclfhiadc98fgdci0mbkkdnadTmbinaiae86bbaicefhiadcufgdmbkkabkk83dbcj:Gdk8Kbbbbdbbblbbbwbbbbbbbebbbdbbblbbbwbbbbc:K:Gdkl8W:qbb",e="b9H79TebbbeKl9Gbb9Gvuuuuueu9Giuuub9Geueuixkbbebeeddddilve9Weeeviebeoweuecj:Gdkr;Neqo9TW9T9VV95dbH9F9F939H79T9F9J9H229F9Jt9VV7bb8A9TW79O9V9Wt9F9KW9J9V9KW9wWVtW949c919M9MWVbdY9TW79O9V9Wt9F9KW9J9V9KW69U9KW949c919M9MWVblE9TW79O9V9Wt9F9KW9J9V9KW69U9KW949tWG91W9U9JWbvL9TW79O9V9Wt9F9KW9J9V9KWS9P2tWV9p9JtboK9TW79O9V9Wt9F9KW9J9V9KWS9P2tWV9r919HtbrL9TW79O9V9Wt9F9KW9J9V9KWS9P2tWVT949WbwY9TW79O9V9Wt9F9KW9J9V9KWS9P2tWVJ9V29VVbDl79IV9Rbqq:W9Dklbzik94evu8Jjjjjbcz9Rhbcbheincbhdcbhiinabcwfadfaicjuaead4ceGglE86bbaialfhiadcefgdcw9hmbkaeai86b:q:W:cjbaecitab8Piw83i:q:G:cjbaecefgecjd9hmbkk:JBl8Aud97dur978Jjjjjbcj;kb9Rgv8Kjjjjbc9:hodnalTmbcuhoaiRbbgrc;WeGc:Ge9hmbarcsGgwce0mbc9:hoalcufadcd4cbawEgDadfgrcKcaawEgqaraq0Egk6mbaialfgxar9RhodnadTgmmbavaoad;8qbbkaicefhPcj;abad9Uc;WFbGcjdadca0EhsdndndnadTmbaoadfhzcbhHinaeaH9nmdaxaP9RaD6miabaHad2fhOaPaDfhAasaeaH9RaHasfae6EgCcsfgocl4cifcd4hXavcj;cbfaoc9WGgQcetfhLavcj;cbfaQci2fhKavcj;cbfaQfhYcbh8Aaoc;ab6hEincbh3dnawTmbaPa8Acd4fRbbh3kcbh5avcj;cbfh8Eindndndndna3a5cet4ciGgoc9:fPdebdkaxaA9RaQ6mwdnaQTmbavcj;cbfa5aQ2faAaQ;8qbbkaAaCfhAxdkaQTmeavcj;cbfa5aQ2fcbaQ;8kbxekaxaA9RaX6moaoclVcbawEhraAaXfhocbhidnaEmbaxao9Rc;Gb6mbcbhlina8EalfhidndndndndndnaAalco4fRbbgqciGarfPDbedibledibkaipxbbbbbbbbbbbbbbbbpklbxlkaiaopbblaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLg8Fcdp:mea8FpmbzeHdOiAlCvXoQrLpxiiiiiiiiiiiiiiiip9ogapxiiiiiiiiiiiiiiiip8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Nghcitpbi:q:G:cjbahRb:q:W:cjbghpsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Nggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spklbahaoclffagRb:q:W:cjbfhoxikaiaopbbwaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLpxssssssssssssssssp9ogapxssssssssssssssssp8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Nghcitpbi:q:G:cjbahRb:q:W:cjbghpsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Nggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spklbahaocwffagRb:q:W:cjbfhoxdkaiaopbbbpklbaoczfhoxekaiaopbbdaoRbbghcitpbi:q:G:cjbahRb:q:W:cjbghpsaoRbeggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPpklbahaocdffagRb:q:W:cjbfhokdndndndndndnaqcd4ciGarfPDbedibledibkaiczfpxbbbbbbbbbbbbbbbbpklbxlkaiczfaopbblaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLg8Fcdp:mea8FpmbzeHdOiAlCvXoQrLpxiiiiiiiiiiiiiiiip9ogapxiiiiiiiiiiiiiiiip8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Nghcitpbi:q:G:cjbahRb:q:W:cjbghpsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Nggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spklbahaoclffagRb:q:W:cjbfhoxikaiczfaopbbwaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLpxssssssssssssssssp9ogapxssssssssssssssssp8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Nghcitpbi:q:G:cjbahRb:q:W:cjbghpsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Nggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spklbahaocwffagRb:q:W:cjbfhoxdkaiczfaopbbbpklbaoczfhoxekaiczfaopbbdaoRbbghcitpbi:q:G:cjbahRb:q:W:cjbghpsaoRbeggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPpklbahaocdffagRb:q:W:cjbfhokdndndndndndnaqcl4ciGarfPDbedibledibkaicafpxbbbbbbbbbbbbbbbbpklbxlkaicafaopbblaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLg8Fcdp:mea8FpmbzeHdOiAlCvXoQrLpxiiiiiiiiiiiiiiiip9ogapxiiiiiiiiiiiiiiiip8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Nghcitpbi:q:G:cjbahRb:q:W:cjbghpsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Nggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spklbahaoclffagRb:q:W:cjbfhoxikaicafaopbbwaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLpxssssssssssssssssp9ogapxssssssssssssssssp8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Nghcitpbi:q:G:cjbahRb:q:W:cjbghpsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Nggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spklbahaocwffagRb:q:W:cjbfhoxdkaicafaopbbbpklbaoczfhoxekaicafaopbbdaoRbbghcitpbi:q:G:cjbahRb:q:W:cjbghpsaoRbeggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPpklbahaocdffagRb:q:W:cjbfhokdndndndndndnaqco4arfPDbedibledibkaic8Wfpxbbbbbbbbbbbbbbbbpklbxlkaic8Wfaopbblaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLg8Fcdp:mea8FpmbzeHdOiAlCvXoQrLpxiiiiiiiiiiiiiiiip9ogapxiiiiiiiiiiiiiiiip8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Ngicitpbi:q:G:cjbaiRb:q:W:cjbgipsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Ngqcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spklbaiaoclffaqRb:q:W:cjbfhoxikaic8Wfaopbbwaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLpxssssssssssssssssp9ogapxssssssssssssssssp8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Ngicitpbi:q:G:cjbaiRb:q:W:cjbgipsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Ngqcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spklbaiaocwffaqRb:q:W:cjbfhoxdkaic8Wfaopbbbpklbaoczfhoxekaic8WfaopbbdaoRbbgicitpbi:q:G:cjbaiRb:q:W:cjbgipsaoRbegqcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPpklbaiaocdffaqRb:q:W:cjbfhokalc;abfhialcjefaQ0meaihlaxao9Rc;Fb0mbkkdnaiaQ9pmbaici4hlinaxao9RcK6mwa8EaifhqdndndndndndnaAaico4fRbbalcoG4ciGarfPDbedibledibkaqpxbbbbbbbbbbbbbbbbpkbbxlkaqaopbblaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLg8Fcdp:mea8FpmbzeHdOiAlCvXoQrLpxiiiiiiiiiiiiiiiip9ogapxiiiiiiiiiiiiiiiip8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Nghcitpbi:q:G:cjbahRb:q:W:cjbghpsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Nggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spkbbahaoclffagRb:q:W:cjbfhoxikaqaopbbwaopbbbg8Fclp:mea8FpmbzeHdOiAlCvXoQrLpxssssssssssssssssp9ogapxssssssssssssssssp8Jg8Fp5b9cjF;8;4;W;G;ab9:9cU1:Nghcitpbi:q:G:cjbahRb:q:W:cjbghpsa8Fp5e9cjF;8;4;W;G;ab9:9cU1:Nggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPaaa8Fp9spkbbahaocwffagRb:q:W:cjbfhoxdkaqaopbbbpkbbaoczfhoxekaqaopbbdaoRbbghcitpbi:q:G:cjbahRb:q:W:cjbghpsaoRbeggcitpbi:q:G:cjbp9UpmbedilvorzHOACXQLpPpkbbahaocdffagRb:q:W:cjbfhokalcdfhlaiczfgiaQ6mbkkaohAaoTmoka8EaQfh8Ea5cefg5cl9hmbkdndndndnawTmbaza8Acd4fRbbglciGPlbedwbkaQTmdavcjdfa8Afhlava8Afpbdbh8Jcbhoinalavcj;cbfaofpblbg8KaYaofpblbg8LpmbzeHdOiAlCvXoQrLg8MaLaofpblbg8NaKaofpblbgypmbzeHdOiAlCvXoQrLg8PpmbezHdiOAlvCXorQLg8Fcep9Ta8Fpxeeeeeeeeeeeeeeeegap9op9Hp9rg8Fa8Jp9Ug8Jp9Abbbaladfgla8Ja8Fa8Fpmlvorlvorlvorlvorp9Ug8Jp9Abbbaladfgla8Ja8Fa8FpmwDqkwDqkwDqkwDqkp9Ug8Jp9Abbbaladfgla8Ja8Fa8FpmxmPsxmPsxmPsxmPsp9Ug8Jp9Abbbaladfgla8Ja8Ma8PpmwDKYqk8AExm35Ps8E8Fg8Fcep9Ta8Faap9op9Hp9rg8Fp9Ug8Jp9Abbbaladfgla8Ja8Fa8Fpmlvorlvorlvorlvorp9Ug8Jp9Abbbaladfgla8Ja8Fa8FpmwDqkwDqkwDqkwDqkp9Ug8Jp9Abbbaladfgla8Ja8Fa8FpmxmPsxmPsxmPsxmPsp9Ug8Jp9Abbbaladfgla8Ja8Ka8LpmwKDYq8AkEx3m5P8Es8Fg8Ka8NaypmwKDYq8AkEx3m5P8Es8Fg8LpmbezHdiOAlvCXorQLg8Fcep9Ta8Faap9op9Hp9rg8Fp9Ug8Jp9Abbbaladfgla8Ja8Fa8Fpmlvorlvorlvorlvorp9Ug8Jp9Abbbaladfgla8Ja8Fa8FpmwDqkwDqkwDqkwDqkp9Ug8Jp9Abbbaladfgla8Ja8Fa8FpmxmPsxmPsxmPsxmPsp9Ug8Jp9Abbbaladfgla8Ja8Ka8LpmwDKYqk8AExm35Ps8E8Fg8Fcep9Ta8Faap9op9Hp9rg8Fp9Ugap9Abbbaladfglaaa8Fa8Fpmlvorlvorlvorlvorp9Ugap9Abbbaladfglaaa8Fa8FpmwDqkwDqkwDqkwDqkp9Ugap9Abbbaladfglaaa8Fa8FpmxmPsxmPsxmPsxmPsp9Ug8Jp9AbbbaladfhlaoczfgoaQ6mbxikkaQTmeavcjdfa8Afhlava8Afpbdbh8Jcbhoinalavcj;cbfaofpblbg8KaYaofpblbg8LpmbzeHdOiAlCvXoQrLg8MaLaofpblbg8NaKaofpblbgypmbzeHdOiAlCvXoQrLg8PpmbezHdiOAlvCXorQLg8Fcep:nea8Fpxebebebebebebebebgap9op:bep9rg8Fa8Jp:oeg8Jp9Abbbaladfgla8Ja8Fa8Fpmlvorlvorlvorlvorp:oeg8Jp9Abbbaladfgla8Ja8Fa8FpmwDqkwDqkwDqkwDqkp:oeg8Jp9Abbbaladfgla8Ja8Fa8FpmxmPsxmPsxmPsxmPsp:oeg8Jp9Abbbaladfgla8Ja8Ma8PpmwDKYqk8AExm35Ps8E8Fg8Fcep:nea8Faap9op:bep9rg8Fp:oeg8Jp9Abbbaladfgla8Ja8Fa8Fpmlvorlvorlvorlvorp:oeg8Jp9Abbbaladfgla8Ja8Fa8FpmwDqkwDqkwDqkwDqkp:oeg8Jp9Abbbaladfgla8Ja8Fa8FpmxmPsxmPsxmPsxmPsp:oeg8Jp9Abbbaladfgla8Ja8Ka8LpmwKDYq8AkEx3m5P8Es8Fg8Ka8NaypmwKDYq8AkEx3m5P8Es8Fg8LpmbezHdiOAlvCXorQLg8Fcep:nea8Faap9op:bep9rg8Fp:oeg8Jp9Abbbaladfgla8Ja8Fa8Fpmlvorlvorlvorlvorp:oeg8Jp9Abbbaladfgla8Ja8Fa8FpmwDqkwDqkwDqkwDqkp:oeg8Jp9Abbbaladfgla8Ja8Fa8FpmxmPsxmPsxmPsxmPsp:oeg8Jp9Abbbaladfgla8Ja8Ka8LpmwDKYqk8AExm35Ps8E8Fg8Fcep:nea8Faap9op:bep9rg8Fp:oegap9Abbbaladfglaaa8Fa8Fpmlvorlvorlvorlvorp:oegap9Abbbaladfglaaa8Fa8FpmwDqkwDqkwDqkwDqkp:oegap9Abbbaladfglaaa8Fa8FpmxmPsxmPsxmPsxmPsp:oeg8Jp9AbbbaladfhlaoczfgoaQ6mbxdkkaQTmbcbhocbalcl4gl9Rc8FGhiavcjdfa8Afhrava8Afpbdbhainaravcj;cbfaofpblbg8JaYaofpblbg8KpmbzeHdOiAlCvXoQrLg8LaLaofpblbg8MaKaofpblbg8NpmbzeHdOiAlCvXoQrLgypmbezHdiOAlvCXorQLg8Faip:Rea8Falp:Tep9qg8Faap9rgap9Abbbaradfgraaa8Fa8Fpmlvorlvorlvorlvorp9rgap9Abbbaradfgraaa8Fa8FpmwDqkwDqkwDqkwDqkp9rgap9Abbbaradfgraaa8Fa8FpmxmPsxmPsxmPsxmPsp9rgap9Abbbaradfgraaa8LaypmwDKYqk8AExm35Ps8E8Fg8Faip:Rea8Falp:Tep9qg8Fp9rgap9Abbbaradfgraaa8Fa8Fpmlvorlvorlvorlvorp9rgap9Abbbaradfgraaa8Fa8FpmwDqkwDqkwDqkwDqkp9rgap9Abbbaradfgraaa8Fa8FpmxmPsxmPsxmPsxmPsp9rgap9Abbbaradfgraaa8Ja8KpmwKDYq8AkEx3m5P8Es8Fg8Ja8Ma8NpmwKDYq8AkEx3m5P8Es8Fg8KpmbezHdiOAlvCXorQLg8Faip:Rea8Falp:Tep9qg8Fp9rgap9Abbbaradfgraaa8Fa8Fpmlvorlvorlvorlvorp9rgap9Abbbaradfgraaa8Fa8FpmwDqkwDqkwDqkwDqkp9rgap9Abbbaradfgraaa8Fa8FpmxmPsxmPsxmPsxmPsp9rgap9Abbbaradfgraaa8Ja8KpmwDKYqk8AExm35Ps8E8Fg8Faip:Rea8Falp:Tep9qg8Fp9rgap9Abbbaradfgraaa8Fa8Fpmlvorlvorlvorlvorp9rgap9Abbbaradfgraaa8Fa8FpmwDqkwDqkwDqkwDqkp9rgap9Abbbaradfgraaa8Fa8FpmxmPsxmPsxmPsxmPsp9rgap9AbbbaradfhraoczfgoaQ6mbkka8Aclfg8Aad6mbkdnaCad2goTmbaOavcjdfao;8qbbkdnammbavavcjdfaCcufad2fad;8qbbkaCaHfhHc9:hoaAhPaAmbxlkkaeTmbaDalfhrcbhocuhlinaralaD9RglfaD6mdasaeao9Raoasfae6Eaofgoae6mbkaial9RhPkcbc99axaP9RakSEhoxekc9:hokavcj;kbf8Kjjjjbaokwbz:bjjjbkNsezu8Jjjjjbc;ae9Rgv8Kjjjjbc9:hodnalaeci9UgrcHf6mbcuhoaiRbbgwc;WeGc;Ge9hmbawcsGgDce0mbavc;abfcFecje;8kbav9cu83iUav9cu83i8Wav9cu83iyav9cu83iaav9cu83iKav9cu83izav9cu83iwav9cu83ibaialfc9WfhqaicefgwarfhldnaeTmbcmcsaDceSEhkcbhxcbhmcbhrcbhicbhoindnalaq9nmbc9:hoxikdndnawRbbgDc;Ve0mbavc;abfaoaDcu7gPcl4fcsGcitfgsydlhzasydbhHdndnaDcsGgsak9pmbavaiaPfcsGcdtfydbaxasEhDaxasTgOfhxxekdndnascsSmbcehOasc987asamffcefhDxekalcefhDal8SbbgscFeGhPdndnascu9mmbaDhlxekalcvfhlaPcFbGhPcrhsdninaD8SbbgOcFbGastaPVhPaOcu9kmeaDcefhDascrfgsc8J9hmbxdkkaDcefhlkcehOaPce4cbaPceG9R7amfhDkaDhmkavc;abfaocitfgsaDBdbasazBdlavaicdtfaDBdbavc;abfaocefcsGcitfgsaHBdbasaDBdlaocdfhoaOaifhidnadcd9hmbabarcetfgsaH87ebasclfaD87ebascdfaz87ebxdkabarcdtfgsaHBdbascwfaDBdbasclfazBdbxekdnaDcpe0mbavaiaqaDcsGfRbbgscl4gP9RcsGcdtfydbaxcefgOaPEhDavaias9RcsGcdtfydbaOaPTgzfgOascsGgPEhsaPThPdndnadcd9hmbabarcetfgHax87ebaHclfas87ebaHcdfaD87ebxekabarcdtfgHaxBdbaHcwfasBdbaHclfaDBdbkavaicdtfaxBdbavc;abfaocitfgHaDBdbaHaxBdlavaicefgicsGcdtfaDBdbavc;abfaocefcsGcitfgHasBdbaHaDBdlavaiazfgicsGcdtfasBdbavc;abfaocdfcsGcitfgDaxBdbaDasBdlaocifhoaiaPfhiaOaPfhxxekaxcbalRbbgsEgHaDc;:eSgDfhOascsGhAdndnascl4gCmbaOcefhzxekaOhzavaiaC9RcsGcdtfydbhOkdndnaAmbazcefhxxekazhxavaias9RcsGcdtfydbhzkdndnaDTmbalcefhDxekalcdfhDal8SbegPcFeGhsdnaPcu9kmbalcofhHascFbGhscrhldninaD8SbbgPcFbGaltasVhsaPcu9kmeaDcefhDalcrfglc8J9hmbkaHhDxekaDcefhDkasce4cbasceG9R7amfgmhHkdndnaCcsSmbaDhsxekaDcefhsaD8SbbglcFeGhPdnalcu9kmbaDcvfhOaPcFbGhPcrhldninas8SbbgDcFbGaltaPVhPaDcu9kmeascefhsalcrfglc8J9hmbkaOhsxekascefhskaPce4cbaPceG9R7amfgmhOkdndnaAcsSmbashlxekascefhlas8SbbgDcFeGhPdnaDcu9kmbascvfhzaPcFbGhPcrhDdninal8SbbgscFbGaDtaPVhPascu9kmealcefhlaDcrfgDc8J9hmbkazhlxekalcefhlkaPce4cbaPceG9R7amfgmhzkdndnadcd9hmbabarcetfgDaH87ebaDclfaz87ebaDcdfaO87ebxekabarcdtfgDaHBdbaDcwfazBdbaDclfaOBdbkavc;abfaocitfgDaOBdbaDaHBdlavaicdtfaHBdbavc;abfaocefcsGcitfgDazBdbaDaOBdlavaicefgicsGcdtfaOBdbavc;abfaocdfcsGcitfgDaHBdbaDazBdlavaiaCTaCcsSVfgicsGcdtfazBdbaiaATaAcsSVfhiaocifhokawcefhwaocsGhoaicsGhiarcifgrae6mbkkcbc99alaqSEhokavc;aef8Kjjjjbaok:clevu8Jjjjjbcz9Rhvdnalaecvf9pmbc9:skdnaiRbbc;:eGc;qeSmbcuskav9cb83iwaicefhoaialfc98fhrdnaeTmbdnadcdSmbcbhwindnaoar6mbc9:skaocefhlao8SbbgicFeGhddndnaicu9mmbalhoxekaocvfhoadcFbGhdcrhidninal8SbbgDcFbGaitadVhdaDcu9kmealcefhlaicrfgic8J9hmbxdkkalcefhokabawcdtfadc8Etc8F91adcd47avcwfadceGcdtVglydbfgiBdbalaiBdbawcefgwae9hmbxdkkcbhwindnaoar6mbc9:skaocefhlao8SbbgicFeGhddndnaicu9mmbalhoxekaocvfhoadcFbGhdcrhidninal8SbbgDcFbGaitadVhdaDcu9kmealcefhlaicrfgic8J9hmbxdkkalcefhokabawcetfadc8Etc8F91adcd47avcwfadceGcdtVglydbfgi87ebalaiBdbawcefgwae9hmbkkcbc99aoarSEk;Toio97eue97aec98Ghedndnadcl9hmbaeTmecbhdinababpbbbgicKp:RecKp:Sep;6eglaicwp:RecKp:Sep;6ealp;Geaiczp:RecKp:Sep;6egvp;Gep;Kep;Legopxbbbbbbbbbbbbbbbbp:2egralpxbbbjbbbjbbbjbbbjgwp9op9rp;Keglpxbb;:9cbb;:9cbb;:9cbb;:9calalp;Meaoaop;Meavaravawp9op9rp;Keglalp;Mep;Kep;Kep;Jep;Negvp;Mepxbbn0bbn0bbn0bbn0grp;KepxFbbbFbbbFbbbFbbbp9oaipxbbbFbbbFbbbFbbbFp9op9qalavp;Mearp;Kecwp:RepxbFbbbFbbbFbbbFbbp9op9qaoavp;Mearp;Keczp:RepxbbFbbbFbbbFbbbFbp9op9qpkbbabczfhbadclfgdae6mbxdkkaeTmbcbhdinabczfgDaDpbbbgipxbbbbbbFFbbbbbbFFgwp9oabpbbbgoaipmbediwDqkzHOAKY8AEgvczp:Reczp:Sep;6eglaoaipmlvorxmPsCXQL358E8FpxFubbFubbFubbFubbp9op;6eavczp:Sep;6egvp;Gealp;Gep;Kep;Legipxbbbbbbbbbbbbbbbbp:2egralpxbbbjbbbjbbbjbbbjgqp9op9rp;Keglpxb;:FSb;:FSb;:FSb;:FSalalp;Meaiaip;Meavaravaqp9op9rp;Keglalp;Mep;Kep;Kep;Jep;Negvp;Mepxbbn0bbn0bbn0bbn0grp;KepxFFbbFFbbFFbbFFbbp9oaiavp;Mearp;Keczp:Rep9qgialavp;Mearp;KepxFFbbFFbbFFbbFFbbp9oglpmwDKYqk8AExm35Ps8E8Fp9qpkbbabaoawp9oaialpmbezHdiOAlvCXorQLp9qpkbbabcafhbadclfgdae6mbkkk;2ileue97euo97dnaec98GgiTmbcbheinabcKfpx:ji:1S:ji:1S:ji:1S:ji:1SabpbbbglabczfgvpbbbgopmlvorxmPsCXQL358E8Fgrczp:Segwpxibbbibbbibbbibbbp9qp;6egDp;NegqaDaDp;MegDaDp;KealaopmbediwDqkzHOAKY8AEgDczp:Reczp:Sep;6eglalp;MeaDczp:Sep;6egoaop;Mearczp:Reczp:Sep;6egrarp;Mep;Kep;Kep;Lepxbbbbbbbbbbbbbbbbp:4ep;Jep;Mepxbbn0bbn0bbn0bbn0gDp;KepxFFbbFFbbFFbbFFbbgkp9oaqaop;MeaDp;Keczp:Rep9qgoaqalp;MeaDp;Keakp9oaqarp;MeaDp;Keczp:Rep9qgDpmwDKYqk8AExm35Ps8E8Fglp5eawclp:RegqpEi:T:j83ibavalp5baqpEd:T:j83ibabcwfaoaDpmbezHdiOAlvCXorQLgDp5eaqpEe:T:j83ibabaDp5baqpEb:T:j83ibabcafhbaeclfgeai6mbkkkuee97dnadcd4ae2c98GgeTmbcbhdinababpbbbgicwp:Recwp:Sep;6eaicep:SepxbbjFbbjFbbjFbbjFp9opxbbjZbbjZbbjZbbjZp:Uep;Mepkbbabczfhbadclfgdae6mbkkk:Sodw97euaec98Ghedndnadcl9hmbaeTmecbhdinabpxbbuJbbuJbbuJbbuJabpbbbgicKp:TeglaicYp:Tep9qgvcdp:Teavp9qgvclp:Teavp9qgop;6ep;Negvaicwp:RecKp:SegraipxFbbbFbbbFbbbFbbbgwp9ogDp:Uep;6ep;Mepxbbn0bbn0bbn0bbn0gqp;Kecwp:RepxbFbbbFbbbFbbbFbbp9oavaDarp:Xeaiczp:RecKp:Segip:Uep;6ep;Meaqp;Keawp9op9qavaDaraip:Uep:Xep;6ep;Meaqp;Keczp:RepxbbFbbbFbbbFbbbFbp9op9qavaoalcep:Rep9oalpxebbbebbbebbbebbbp9op9qp;6ep;Meaqp;KecKp:Rep9qpkbbabczfhbadclfgdae6mbxdkkaeTmbcbhdinabczfgkpxbFu9hbFu9hbFu9hbFu9habpbbbglakpbbbgrpmlvorxmPsCXQL358E8Fgvczp:TegqavcHp:Tep9qgicdp:Teaip9qgiclp:Teaip9qgicwp:Teaip9qgop;6ep;NegialarpmbediwDqkzHOAKY8AEgDpxFFbbFFbbFFbbFFbbglp9ograDczp:Segwp:Ueavczp:Reczp:SegDp:Xep;6ep;Mepxbbn0bbn0bbn0bbn0gvp;Kealp9oaiarawaDp:Uep:Xep;6ep;Meavp;Keczp:Rep9qgwaiaoaqcep:Rep9oaqpxebbbebbbebbbebbbp9op9qp;6ep;Meavp;Keczp:ReaiaDarp:Uep;6ep;Meavp;Kealp9op9qgipmwDKYqk8AExm35Ps8E8FpkbbabawaipmbezHdiOAlvCXorQLpkbbabcafhbadclfgdae6mbkkk9teiucbcbydj:G:cjbgeabcifc98GfgbBdj:G:cjbdndnabZbcztgd9nmbcuhiabad9RcFFifcz4nbcuSmekaehikaikkxebcj:Gdklz:zbb",t=new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,3,2,0,0,5,3,1,0,1,12,1,0,10,22,2,12,0,65,0,65,0,65,0,252,10,0,0,11,7,0,65,0,253,15,26,11]),n=new Uint8Array([32,0,65,2,1,106,34,33,3,128,11,4,13,64,6,253,10,7,15,116,127,5,8,12,40,16,19,54,20,9,27,255,113,17,42,67,24,23,146,148,18,14,22,45,70,69,56,114,101,21,25,63,75,136,108,28,118,29,73,115]);if(typeof WebAssembly!="object")return{supported:!1};var s=WebAssembly.validate(t)?o(e):o(i),r,a=WebAssembly.instantiate(s,{}).then(function(p){r=p.instance,r.exports.__wasm_call_ctors()});function o(p){for(var _=new Uint8Array(p.length),S=0;S<p.length;++S){var x=p.charCodeAt(S);_[S]=x>96?x-97:x>64?x-39:x+4}for(var M=0,S=0;S<p.length;++S)_[M++]=_[S]<60?n[_[S]]:(_[S]-60)*64+_[++S];return _.buffer.slice(0,M)}function c(p,_,S,x,M,E,A){var y=p.exports.sbrk,w=x+3&-4,R=y(w*M),I=y(E.length),L=new Uint8Array(p.exports.memory.buffer);L.set(E,I);var F=_(R,x,M,I,E.length);if(F==0&&A&&A(R,w,M),S.set(L.subarray(R,R+x*M)),y(R-y(0)),F!=0)throw new Error("Malformed buffer data: "+F)}var l={NONE:"",OCTAHEDRAL:"meshopt_decodeFilterOct",QUATERNION:"meshopt_decodeFilterQuat",EXPONENTIAL:"meshopt_decodeFilterExp",COLOR:"meshopt_decodeFilterColor"},h={ATTRIBUTES:"meshopt_decodeVertexBuffer",TRIANGLES:"meshopt_decodeIndexBuffer",INDICES:"meshopt_decodeIndexSequence"},u=[],d=0;function f(p){var _={object:new Worker(p),pending:0,requests:{}};return _.object.onmessage=function(S){var x=S.data;_.pending-=x.count,_.requests[x.id][x.action](x.value),delete _.requests[x.id]},_}function m(p){for(var _="self.ready = WebAssembly.instantiate(new Uint8Array(["+new Uint8Array(s)+"]), {}).then(function(result) { result.instance.exports.__wasm_call_ctors(); return result.instance; });self.onmessage = "+g.name+";"+c.toString()+g.toString(),S=new Blob([_],{type:"text/javascript"}),x=URL.createObjectURL(S),M=u.length;M<p;++M)u[M]=f(x);for(var M=p;M<u.length;++M)u[M].object.postMessage({});u.length=p,URL.revokeObjectURL(x)}function b(p,_,S,x,M){for(var E=u[0],A=1;A<u.length;++A)u[A].pending<E.pending&&(E=u[A]);return new Promise(function(y,w){var R=new Uint8Array(S),I=++d;E.pending+=p,E.requests[I]={resolve:y,reject:w},E.object.postMessage({id:I,count:p,size:_,source:R,mode:x,filter:M},[R.buffer])})}function g(p){var _=p.data;self.ready.then(function(S){if(!_.id)return self.close();try{var x=new Uint8Array(_.count*_.size);c(S,S.exports[_.mode],x,_.count,_.size,_.source,S.exports[_.filter]),self.postMessage({id:_.id,count:_.count,action:"resolve",value:x},[x.buffer])}catch(M){self.postMessage({id:_.id,count:_.count,action:"reject",value:M})}})}return{ready:a,supported:!0,useWorkers:function(p){m(p)},decodeVertexBuffer:function(p,_,S,x,M){c(r,r.exports.meshopt_decodeVertexBuffer,p,_,S,x,r.exports[l[M]])},decodeIndexBuffer:function(p,_,S,x){c(r,r.exports.meshopt_decodeIndexBuffer,p,_,S,x)},decodeIndexSequence:function(p,_,S,x){c(r,r.exports.meshopt_decodeIndexSequence,p,_,S,x)},decodeGltfBuffer:function(p,_,S,x,M,E){c(r,r.exports[h[M]],p,_,S,x,r.exports[l[E]])},decodeGltfBufferAsync:function(p,_,S,x,M){return u.length>0?b(p,_,S,h[x],l[M]):a.then(function(){var E=new Uint8Array(p*_);return c(r,r.exports[h[x]],E,p,_,S,r.exports[l[M]]),E})}}})();var bd=new Map,Jy=new kl().setMeshoptDecoder(m0);function Ft(i,e=.15,t=.5){return new mt({color:i,metalness:e,roughness:t})}function Pn(i,e,t,n,s,r,a,o="#d8e8ed"){let c=document.createElement("canvas");c.width=256,c.height=96;let l=c.getContext("2d");l.fillStyle=o,l.font="600 58px Segoe UI, sans-serif",l.textAlign="center",l.textBaseline="middle",l.fillText(e,128,50,224);let h=new xn(c);h.colorSpace=pt;let u=new ke(new Vn(t,n),new jt({map:h,transparent:!0,depthWrite:!1,toneMapped:!1}));u.rotation.x=-Math.PI/2,u.position.set(s,r,a),i.add(u)}function xd(i,e,t,n,s,r,a){let o=document.createElement("canvas");o.width=512,o.height=256,a(o.getContext("2d"));let c=new xn(o);c.colorSpace=pt,c.anisotropy=4;let l=new ke(new Vn(e,t),new jt({map:c,transparent:!0,depthWrite:!1,toneMapped:!1}));l.rotation.x=-Math.PI/2,l.position.set(n,s,r),i.add(l)}function Zy(i,e,t,n){let s=new Jt;e.forEach(([a,o],c)=>c?s.lineTo(a,-o):s.moveTo(a,-o)),s.closePath();let r=new ke(new ys(s,{depth:t,bevelEnabled:!1}),n);return r.rotation.x=-Math.PI/2,r.position.y=-t/2,i.add(r),r}function $y(i,e,t,n,s,r,a){Nt(i,[[0,-.006],[s*.88,-.006],[s,-.002],[s,.004],[s*.76,.009],[0,.009]],e,t,n,r,22);for(let o of[.48,.48+Math.PI/2]){let c=he(i,s*1.03,.0015,s*.2,e,t+.0093,n,a);c.rotation.y=o}}function Vi(i,e,t,n,s,r,a,o){for(let c=0;c<s;c++){let l=e+c*r;he(i,.056,.029,.066,l,t,n,a,.004);for(let h of[-1,1])he(i,.069,.01,.03,l,t-.011,n+h*.041,o,.002),he(i,.056,.015,.012,l,t+.005,n+h*.032,o,.002)}}function vn(i,e,t,n,s){for(let r=1;r<e.length;r++){let[[a,o],[c,l]]=e.slice(r-1,r+1),h=c-a,u=l-o,d=he(i,Math.hypot(h,u)+n,.0015,n,(a+c)/2,t,(o+l)/2,s);d.rotation.y=-Math.atan2(u,h)}}function Mn(i,e,t,n,s,r,a){We(i,s,.002,e,t,n,r,12),We(i,s*.42,.002,e,t+.0015,n,a,10)}function Ds(i,e,t,n,s,r,a){We(i,s,.003,e,t,n,a,14),Nt(i,[[0,-.019],[s*.32,-.016],[s*.55,-.009],[s*.86,-.002],[s*.91,0],[0,0]],e,t,n,r,12)}function Qy(i,e,t,n,s,r,a){let o=new Jt;[[0,0],[.066,0],[.09,.022],[.112,.086],[.156,.103],[.207,.103],[.207,.119],[.15,.119],[.097,.099],[.075,.032],[.058,.015],[0,.015]].forEach(([l,h],u)=>u?o.lineTo(l*s,h):o.moveTo(l*s,h)),o.closePath();let c=Re(i,o,r,e,t,n,a,.0013,3);return c.rotation.y=Math.PI/2,c}function ev(i,e,t,n,s,r){Nt(i,[[.043,-.01],[.081,-.01],[.085,.002],[.078,.022],[.066,.026],[.056,.024],[.051,.012],[.043,.01],[.043,-.01]],e,t,n,s,36),We(i,.042,.008,e,t+.004,n,r,24),We(i,.017,.018,e,t+.013,n,s,20)}function tv(i,e,t){let{metal:n,black:s,blue:r}=t,a=3.9,o=a*1.512/2.64,c=1.864,l=Ft(1383462,.3,.41),h=Ft(2568510,.14,.66),u=Ft(857379,.26,.27);Re(i,$(a,o,.057),.057,0,c,0,l,.007,13),Re(i,$(a-.045,o-.034,.042),.008,0,c,.036,u,.002,12);let d=a*2.578/2.64,f=d*9/16,m=new Di($(d,f,.018),10),b=m.attributes.position,g=m.attributes.uv;for(let S=0;S<g.count;S++)g.setXY(S,b.getX(S)/d+.5,b.getY(S)/f+.5);let p=new ke(m,new jt({map:e||null,color:e?16777215:2379127,toneMapped:!1}));p.position.set(0,c+.013,.045),i.add(p),Nl(i,a,0,c,0,{shell:h,dark:s,metal:n,trim:l,portBlue:r});let _=$(.225,1.65,.049);_.holes.push($(.101,.269,.048,0,-.376,new Qe)),Re(i,_,.153,0,.94,-.214,n,.007,8),he(i,.228,.405,.068,0,1.471,-.302,h,.021),he(i,.444,.354,.096,0,1.706,-.201,l,.026),We(i,.065,.325,0,1.705,-.256,n,30).rotation.z=Math.PI/2,_e(i,1.48,.768,.113,.049,0,.087,-.135,l,.011,12),_e(i,1.34,.657,.092,.015,0,.048,-.135,s,.003,10);for(let S of[-.528,.528])_e(i,.118,.415,.049,.006,S,.039,-.133,h,.002,7);he(i,.033,.006,.003,1.718,c-o/2+.016,.044,r,.001)}function nv(i,e){let{pcb:t,metal:n,black:s,gold:r,traceInk:a,silk:o}=e,c=$(2.59,1.65,.112);for(let h of[-1.08,1.08])for(let u of[-.603,.603])Vt(c,.057,h,u);Re(i,c,.077,0,0,0,t,.005,10).rotation.x=-Math.PI/2;for(let h of[-1.08,1.08])for(let u of[-.603,.603])Nt(i,[[.057,0],[.083,0],[.083,.004],[.057,.004],[.057,0]],h,.041,u,r,26);for(let h of[-1,1])for(let u=0;u<4;u++){let d=-.449+u*.298,f=h*.59;_e(i,.143,.185,.018,.009,d,.047,f,r,.001,6),Qy(i,d,.052,f,h,.085,n);for(let m of[-.034,.034]){let b=new ke(new vs(.017,10,6),n);b.scale.set(1.1,.41,1.65),b.position.set(d+m,.053,f+h*.021),i.add(b)}vn(i,[[d,f+h*.015],[d+(u-1.5)*.073,f+h*.14],[d+(u-1.5)*.073,f+h*.205]],.043,.016,a),Mn(i,d+(u-1.5)*.073,.043,f+h*.205,.018,r,s)}_e(i,1.259,.846,.052,.116,0,.194,0,s,.018,12);let l=$(1.145,.73,.059);Vt(l,.041,-.454,.243),Re(i,l,.004,0,.274,0,Ft(2700097,.12,.66),0,12).rotation.x=-Math.PI/2,We(i,.03,.003,-.454,.269,-.243,s,24),Pn(i,"SPI FLASH",.878,.202,.042,.279,.012,"#abbac7"),Pn(i,"SOIC  \u2022  8",.547,.093,.038,.279,.188,"#8392a1"),Vi(i,-1.148,.063,-.246,3,.178,s,n),Vi(i,.72,.063,.316,3,.163,s,n);for(let h of[-.97,.969])for(let u of[-.32,-.16,.02,.18,.34])Mn(i,h,.043,u,.013,n,s);for(let h of[-1,1])for(let u=0;u<3;u++){let d=-.387+u*.215;vn(i,[[h*.675,d],[h*.752,d],[h*.873,d+.084],[h*1.008,d+.084]],.044,.012,a),Mn(i,h*.674,.044,d,.011,r,s)}for(let h of[-.501,.501])he(i,.251,.002,.01,.82,.041,h,o);for(let[h,u]of[[-.866,.402],[.896,-.414]])for(let d=0;d<3;d++)We(i,.033,.003,h+(d-1)*.083,.044,u,r,16),We(i,.018,.002,h+(d-1)*.083,.047,u,n,14);for(let h of[-1,1])for(let u=0;u<4;u++){let d=-.449+u*.298,f=h*(.605+u%2*.082),m=d+(u-1.5)*.083;Ds(i,d,-.04,f,.026,n,r),vn(i,[[d,f],[m,f-h*.117],[m,f-h*.329],[m*.67,f-h*.397]],-.04,.009,a),Ds(i,m*.67,-.041,f-h*.397,.015,n,r)}for(let h of[-.84,.84]){_e(i,.212,.146,.018,.006,h,-.044,0,r,.001,5);for(let u of[-.039,.039])_e(i,.16,.023,.005,.004,h,-.048,u,n,0,5)}Pn(i,"U1",.254,.103,-.785,.046,-.51,"#9aafa3"),Pn(i,"TP",.207,.103,.877,.046,.52,"#9aafa3")}function iv(i,e){let{pcb:t,metal:n,black:s,gold:r,traceInk:a,silk:o,blue:c}=e;_e(i,2.53,2.53,.134,.095,0,0,0,t,.008,13),_e(i,2.468,2.468,.117,.013,0,-.056,0,Ft(1389361,.16,.65),.003,11),_e(i,2.098,2.098,.134,.048,0,.08,0,s,.013,13),_e(i,1.385,1.225,.046,.057,0,.136,0,n,.008,12),_e(i,1.25,1.094,.047,.04,0,.186,0,c,.006,12),xd(i,1.147,.981,0,.214,0,m=>{m.fillStyle="rgba(186,217,224,.34)",m.strokeStyle="rgba(185,220,232,.44)",m.lineWidth=1.2;for(let b=0;b<4;b++)for(let g=0;g<6;g++){let p=51+g*60,_=35+b*44;m.strokeRect(p,_,52,36);for(let S=0;S<5;S++)for(let x=0;x<3;x++)m.fillRect(p+5+S*8,_+4+x*10,5,6)}m.lineWidth=.85;for(let b=0;b<7;b++)m.strokeRect(21+b*4,15+b*3,472-b*8,223-b*6),m.fillRect(97+b*43,214,36,5)});for(let m of[-.778,.778])Vi(i,-.899,.127,m,12,.163,s,n),he(i,1.869,.002,.015,0,.109,m+Math.sign(m)*.128,o);for(let m of[-.97,.97])for(let b=0;b<9;b++)he(i,.086,.019,.035,m,.121,-.625+b*.157,b%3?n:r,.003);for(let m of[-1,1])for(let b=0;b<21;b++){let g=(b-10)*.1;vn(i,[[g,m*1.23],[g,m*1.182],[g*.96,m*1.124]],.049,.009,a)}for(let m of[-1.123,1.123])for(let b=0;b<12;b++)Mn(i,m,.054,-.89+b*.161,.012,r,s);let l=$(1.449,1.286,.052);l.holes.push($(1.37,1.22,.039,0,0,new Qe)),Re(i,l,.012,0,.11,0,s,.001,8).rotation.x=-Math.PI/2;for(let m of[-1.122,1.122])for(let b of[-1.122,1.122]){let g=Math.sign(m),p=Math.sign(b);he(i,.104,.002,.012,m+g*.016,.053,b+p*.084,o),he(i,.012,.002,.105,m+g*.074,.053,b+p*.038,o),Mn(i,m,.053,b,.021,r,s)}let h=new vs(.031,9,5),u=[];for(let m=-14;m<=14;m++)for(let b=-14;b<=14;b++)Math.max(Math.abs(m),Math.abs(b))>=8&&Math.abs(m)+Math.abs(b)<27&&u.push([m*.079,b*.079]);let d=new ri(h,n,u.length),f=new Ze;u.forEach(([m,b],g)=>d.setMatrixAt(g,f.makeTranslation(m,-.076,b))),i.add(d),Pn(i,"NPU",.596,.165,0,.217,.03,"#dce6ee")}function x0(i,e){let t=new Jt,n=(s,r,a)=>[s[0]+(r[0]-s[0])*a,s[1]+(r[1]-s[1])*a];for(let[s,r]of i.entries()){let a=i[(s+i.length-1)%i.length],o=i[(s+1)%i.length],c=n(r,a,Math.min(.36,e/Math.hypot(a[0]-r[0],a[1]-r[1]))),l=n(r,o,Math.min(.36,e/Math.hypot(o[0]-r[0],o[1]-r[1])));s?t.lineTo(...c):t.moveTo(...c),t.quadraticCurveTo(...r,...l)}return t.closePath(),t}function g0(i=1){let e=[[-.825,-.863],[-.635,-.863],[-.594,-.822],[-.594,-.708],[-.335,-.708],[-.335,-.814],[.335,-.814],[.335,-.708],[.594,-.708],[.594,-.822],[.635,-.863],[.825,-.863]],t=[];for(let n=0;n<4;n++)for(let[s,r]of e){let a=n===0?[s,r]:n===1?[-r,s]:n===2?[-s,-r]:[r,-s];t.push(a.map(o=>o*i))}return x0(t,.016*i)}function sv(i,e,t){let n=document.createElement("canvas");n.width=512,n.height=512;let s=n.getContext("2d"),r=s.createLinearGradient(0,0,0,512);r.addColorStop(0,"#c8cdd0"),r.addColorStop(.48,"#e3e6e7"),r.addColorStop(1,"#d0d5d9"),s.fillStyle=r,s.fillRect(0,0,512,512);let a=74516;for(let f=0;f<980;f++){a=Math.imul(a,1664525)+1013904223>>>0;let m=(a&511)-60,b=a>>>9&511,g=110+(a>>>18&383);s.fillStyle=f%3?"rgba(41,49,58,.045)":"rgba(255,255,255,.09)",s.fillRect(m,b,g,1)}let o=new Path2D("M18.324 9.137l1.559 1.56h2.556v2.557L24 14.814V9.137zM2 9.52l-2 4.96h1.309l.37-.982H3.9l.408.982h1.338L3.432 9.52zm4.209 0v4.955h1.238v-3.092l1.338 1.562h.188l1.338-1.556v3.091h1.238V9.52H10.47l-1.592 1.845L7.287 9.52zm6.283 0v4.96h2.057c1.979 0 2.88-1.046 2.88-2.472 0-1.36-.937-2.488-2.747-2.488zm1.237.91h.792c1.17 0 1.63.711 1.63 1.57 0 .728-.372 1.572-1.616 1.572h-.806zm-10.985.273l.791 1.932H2.008zm17.137.307l-1.604 1.603v2.25h2.246l1.604-1.607h-2.246z");s.fillStyle="#3b444b",s.save(),s.translate(158,183),s.scale(8.15,8.15),s.translate(0,-9.137),s.fill(o),s.restore(),s.font="600 53px Segoe UI, sans-serif",s.textAlign="center",s.textBaseline="middle",s.fillText("RYZEN",251,266,199),s.fillStyle="#535d65",s.font="15px monospace",s.textAlign="left",s.fillText("AM5  \u2022  LAND GRID ARRAY",105,340);for(let f=0;f<8;f++)for(let m=0;m<8;m++)(f===0||m===0||(f*13+m*17+f*m)%7<3)&&s.fillRect(357+m*4,326+f*4,2.7,2.7);let c=new Di(e,10),l=c.attributes.position,h=c.attributes.uv;for(let f=0;f<h.count;f++)h.setXY(f,l.getX(f)/1.76+.5,l.getY(f)/1.76+.5);let u=new xn(n);u.colorSpace=pt,u.anisotropy=4;let d=new ke(c,new mt({map:u,metalness:.49,roughness:.43}));d.rotation.x=-Math.PI/2,d.position.y=t,i.add(d)}function rv(i,e,t){let{pcb:n,metal:s,black:r,gold:a,traceInk:o,silk:c}=t,l=e==="amd",h=l?2.025:1.93,u=l?2.025:2.17,d=h/2,f=u/2,m=x0([[-d,-f],[d,-f],[d,-.59],[d-.07,-.59],[d-.07,-.44],[d,-.44],[d,f],[-d,f],[-d,.44],[-d+.07,.44],[-d+.07,.59],[-d,.59]],.029);if(Re(i,m,.077,0,0,0,n,.004,8).rotation.x=-Math.PI/2,Re(i,m,.008,0,-.041,0,Ft(3560521,.29,.53),0,8).rotation.x=-Math.PI/2,l){let w=Ft(6451573,.57,.41),R=g0(),I=g0(.987);_e(i,1.305,1.305,.082,.03,0,.056,0,r,.005,10),Re(i,R,.044,0,.102,0,w,.012,9).rotation.x=-Math.PI/2,Re(i,I,.108,0,.167,0,s,.014,10).rotation.x=-Math.PI/2,sv(i,I,.236);let L=Ft(9073756,.15,.61);for(let F of["x","z"])for(let D of[-1,1])for(let k of[-1,1])for(let W=0;W<3;W++){let X=k*.465+(W-1)*.064,re=D*(.829+W%2*.029),j=F==="z"?X:re,ee=F==="z"?re:X;he(i,F==="z"?.047:.066,.022,F==="z"?.066:.047,j,.061,ee,W%2?r:L,.003);for(let te of[-1,1])he(i,F==="z"?.043:.015,.012,F==="z"?.015:.043,j+(F==="x"?te*.031:0),.058,ee+(F==="z"?te*.031:0),s,.001)}for(let F=0;F<10;F++)for(let D of[-1,1]){let k=-.862+F*.185;Mn(i,k,.042,D*.954,.009,a,r),Mn(i,D*.955,.042,k,.008,o,r)}}else{_e(i,h*.87,u*.89,.077,.031,0,.055,0,r,.006,11),_e(i,1.621,1.863,.158,.052,0,.102,0,s,.018,14),_e(i,1.512,1.756,.144,.104,0,.171,0,s,.032,14),Pn(i,"PROCESSOR",.867,.163,0,.257,0,"#415467");for(let w of[-.779,.779])for(let R of[-.821,.821])Mn(i,w,.058,R,.015,o,r)}let b=new Jt;b.moveTo(0,0),b.lineTo(.1,0),b.lineTo(0,.1),b.closePath(),Re(i,b,.002,-h/2+.079,.044,u/2-.077,a).rotation.x=-Math.PI/2;let g=l?{count:44,pitchX:.043,pitchZ:.043,r:.0146}:{count:36,pitchX:.05,pitchZ:.056,r:.018},p=[],{count:_,pitchX:S,pitchZ:x,r:M}=g;for(let w=0;w<_;w++)for(let R=0;R<_;R++){let I=R-(_-1)/2,L=w-(_-1)/2;Math.max(Math.abs(I),Math.abs(L))<(l?6.5:6)||Math.abs(I)+Math.abs(L)>_-5||Math.abs(I)>_*.42&&Math.abs(L+Math.sign(I)*12)<2||p.push([I*S,L*x])}let E=new Ks(M,9).scale(1,.83,1).rotateX(Math.PI/2),A=new ri(E,a,p.length),y=new Ze;p.forEach(([w,R],I)=>A.setMatrixAt(I,y.makeTranslation(w,-.047,R))),i.add(A);for(let w of[-.177,0,.177])for(let R=0;R<7;R++){let I=-.196+R*.065;he(i,.087,.019,.034,w,-.053,I,R%2?r:o,.003);for(let L of[-1,1])he(i,.015,.014,.036,w+L*.042,-.053,I,s,.001)}for(let w of[-1,1])for(let R=0;R<7;R++){let I=-.196+R*.065,L=w*.224;vn(i,[[L,I],[L+w*.021,I],[L+w*.036,I+(R<3?-.015:.015)]],-.047,.006,o),Ds(i,L+w*.036,-.048,I+(R<3?-.015:.015),.008,s,a)}for(let w of[-.111,.111])for(let R of[-.255,.255])he(i,.056,.004,.02,w,-.048,R,c,.001),he(i,.009,.004,.043,w+Math.sign(w)*.027,-.048,R,c,.001)}function av(i,e){let{pcb:t,metal:n,black:s,gold:r,traceInk:a,silk:o}=e,c=new Jt;c.moveTo(-1.05,-1.43),c.lineTo(-.17,-1.43),c.quadraticCurveTo(-.142,-1.28,0,-1.28),c.quadraticCurveTo(.142,-1.28,.17,-1.43),c.lineTo(1.05,-1.43),c.quadraticCurveTo(1.1,-1.43,1.1,-1.38);for(let[b,g]of[[1.1,1.43],[.502,1.43],[.502,1.211],[.355,1.211],[.355,1.43],[-1.1,1.43],[-1.1,-1.38]])c.lineTo(b,g);c.quadraticCurveTo(-1.1,-1.43,-1.05,-1.43),c.closePath();let l=Re(i,c,.066,0,0,0,t,.003,13);l.rotation.x=Math.PI/2;for(let b of[-1,1])for(let g=0;g<24;g++){let p=-1.001+g*.088;p>.322&&p<.536||(_e(i,.054,.197,.008,.003,p,b*.036,1.321,r,0,5),b===1&&vn(i,[[p,1.218],[p,1.141],[p*.94,1.09]],.035,.008,a))}let h=new Jt;h.moveTo(-.235,1.427),h.quadraticCurveTo(-.218,1.205,0,1.205),h.quadraticCurveTo(.218,1.205,.235,1.427),h.lineTo(.17,1.427),h.quadraticCurveTo(.143,1.274,0,1.274),h.quadraticCurveTo(-.143,1.274,-.17,1.427),h.closePath(),Re(i,h,.003,0,.037,0,r,0,14).rotation.x=-Math.PI/2;let u=Ft(10266797,.58,.39),d=Ft(5400183,.61,.38);_e(i,1.904,1.481,.084,.026,0,.052,-.068,d,.006,12),_e(i,1.813,1.386,.079,.092,0,.109,-.066,u,.015,12);let f=$(1.671,1.238,.061);f.holes.push($(1.588,1.155,.047,0,0,new Qe)),Re(i,f,.002,0,.171,-.066,n,0,10).rotation.x=-Math.PI/2;for(let b of[-.712,.712]){ev(i,b,.061,-1.039,r,s),Mn(i,b*.68,.038,-.993,.018,n,s);for(let g of[-.903,-.797])he(i,.052,.002,.01,b,.038,g,o);for(let g of[-.064,.064])for(let p of[-.058,.058])he(i,.046,.009,.032,b+p,.04,-1.039+g,n,.002)}for(let b of[.835,.976])Vi(i,-.847,.054,b,9,.211,s,n);for(let b of[-1.024,1.024])for(let g=0;g<10;g++)Mn(i,b,.036,-.658+g*.154,.015,n,s);for(let b=0;b<12;b++)Mn(i,-.926+b*.163,.036,-.838,.01,r,s);Pn(i,"MAIN",.278,.103,-.711,.039,-1.201,"#aabcae"),Pn(i,"AUX",.243,.103,.711,.039,-1.201,"#aabcae");for(let b of[-.685,.685])for(let g of[-.601,.46])We(i,.022,.002,b,.171,g,n,14);for(let b of[-.918,.918])for(let g=0;g<5;g++){let p=-.647+g*.28;he(i,.039,.036,.063,b,.052,p,d,.001),_e(i,.056,.064,.009,.005,b+Math.sign(b)*.015,.038,p,n,0,5)}for(let b of[-.672,.672]){_e(i,.356,.574,.028,.052,b,-.062,.292,s,.005,7);for(let g=0;g<8;g++){let p=.046+g*.071,_=b+Math.sign(b)*.178;he(i,.083,.01,.022,_,-.056,p,n,.001),vn(i,[[_,p],[_+Math.sign(b)*.057,p],[_+Math.sign(b)*.074,p+.02]],-.037,.007,a),Ds(i,_+Math.sign(b)*.074,-.037,p+.02,.011,n,r)}}let m=$(.618,.451,.039);m.holes.push($(.559,.398,.029,0,0,new Qe)),Re(i,m,.008,0,-.039,-.493,n,0,8).rotation.x=Math.PI/2,xd(i,1.447,.754,0,.174,-.075,b=>{b.fillStyle="#313f48",b.font="600 44px Segoe UI",b.fillText("WIRELESS MODULE",34,65),b.font="25px monospace",b.fillText("M.2 2230  /  RF",35,115),b.strokeStyle="#697782",b.strokeRect(35,141,442,1),b.font="19px monospace",b.fillText("MAIN  \u2022  AUX",35,184);for(let g=0;g<31;g++){let p=g%5===0?4:1.5;b.fillRect(330+g*4,160,p,37+g%3*3)}})}function ov(i,e){let{black:t,metal:n,gold:s,pcb:r}=e,a=Ft(4082777,.39,.59),o=Ft(2436409,.15,.64),c=Ft(3425364,.11,.55),l=Ft(5070956,.06,.76),h=$(3.69,1.97,.119);for(let u of[-1.697,1.697])for(let d of[-.78,.78])Vt(h,.057,u,d);Re(i,h,.078,0,-.016,0,t,.009,11).rotation.x=-Math.PI/2;for(let u of[-1.077,0,1.077]){_e(i,1.034,1.643,.147,.095,u,.072,.108,o,.019,12),_e(i,1.001,1.572,.143,.089,u,.118,.105,c,.019,12),_e(i,.903,1.43,.093,.005,u,.184,.115,a,.002,12);for(let d of[-.585,.77])_e(i,.896,.049,.014,.005,u,.184,d,o,.002,6);for(let d of[-.112,.112])_e(i,.138,.133,.016,.011,u+d,.142,-.71,n,.002,6);for(let[d,f,m]of[[u-.385,-.488,-.22],[u+.382,.689,-.25]]){let b=_e(i,.052,.119,.008,.003,d,.188,f,o,0,5);b.rotation.y=m}}for(let u of[-.53,.53]){_e(i,.155,1.217,.047,.008,u,.19,.112,l,.002,8),_e(i,.214,.188,.041,.009,u,.194,.752,l,.003,8);for(let d=0;d<5;d++)he(i,.135,.002,.006,u,.202,.708+d*.022,o)}for(let u of[-1.697,1.697])for(let d of[-.78,.78])Nt(i,[[.046,0],[.077,0],[.074,.008],[.048,.008],[.046,0]],u,.034,d,n,28);_e(i,3.38,.231,.038,.052,0,.054,-.846,r,.006,8);for(let u of[-1.47,-1.04,-.58,.58,1,1.49])Vi(i,u,.09,-.859,2,.108,t,n);_e(i,2.98,.24,.047,.015,-.175,.112,-.862,o,.003,8),_e(i,.459,.266,.043,.081,1.159,.096,-.978,t,.01,9),_e(i,.338,.081,.018,.064,1.159,.103,-1.116,n,.007,9);for(let u=0;u<6;u++){let d=1.019+u*.055;he(i,.028,.022,.025,d,.118,-1.15,s,.002),Rn(i,[[d,.097,-1.135],[d,.113,-1.2],[d+.021,.126,-1.283],[d+.048,.143,-1.327]],.01,u===0?Ft(7093307,.05,.68):o,15,8)}_e(i,.393,.092,.021,.103,1.225,.143,-1.344,t,.006,7),_e(i,.19,.037,.013,.025,1.225,.207,-1.34,o,.003,6);for(let u=0;u<6;u++){let d=1.099+u*.05;he(i,.033,.041,.004,d,.152,-1.394,o,.001),he(i,.018,.014,.003,d,.156,-1.397,s,.001)}for(let u of[-1.077,0,1.077]){for(let f of[-.719,.822]){_e(i,.384,.117,.028,.007,u,-.061,f,l,.003,8);for(let m=0;m<6;m++)he(i,.006,.003,.071,u+(m-2.5)*.045,-.066,f,o)}let d=_e(i,.06,1.141,.025,.007,u+.397,-.06,.092,o,.002,7);d.rotation.y=.026}for(let u of[.997,1.45]){let d=_e(i,.157,.112,.019,.014,u,.088,-1.138,c,.002,6);d.rotation.y=u<1.1?.22:-.22}_e(i,.764,.558,.047,.005,0,.191,.205,o,.002,9),xd(i,.718,.503,0,.197,.203,u=>{u.fillStyle="#d6dfe7",u.font="600 54px Segoe UI",u.fillText("Li-ion",24,64),u.font="27px Segoe UI",u.fillText("RECHARGEABLE BATTERY PACK",24,107,448),u.fillStyle="#a1b5c4",u.fillRect(24,127,458,2),u.font="21px monospace",u.fillText("+ / \u2212   CELL ASSEMBLY",24,165),u.font="41px Segoe UI Symbol",u.fillText("\u267B  \u26A0",24,225);for(let d=0;d<47;d++)u.fillRect(253+d*4,182,d%3?1.4:2.4,36)})}function cv(i,e){let{pcb:t,metal:n,black:s,gold:r,traceInk:a,silk:o}=e,c=new Jt;[[-1.84,-.952],[1.69,-.952],[1.79,-.852],[1.79,.782],[-.371,.782],[-.371,.948],[-.906,.948],[-.906,.85],[-1.003,.85],[-1.003,.948],[-1.242,.948],[-1.242,.782],[-1.84,.782]].forEach(([g,p],_)=>_?c.lineTo(g,-p):c.moveTo(g,-p)),c.closePath();for(let g of[-1.58,1.58])for(let p of[-.7,.7])Vt(c,.043,g,p);Re(i,c,.06,0,0,0,t,.005,8).rotation.x=-Math.PI/2;let l=$(2.17,.393,.033);for(let g=0;g<5;g++)Vt(l,.093,-.758+g*.379,0);let h=Re(i,l,.036,-1.856,.209,0,n,.0045,14);h.rotation.y=-Math.PI/2;let u=$(.166,2.16,.033);Vt(u,.038,0,.95),Re(i,u,.036,-1.912,.429,0,n,.004,8).rotation.x=-Math.PI/2;let d=Re(i,$(.085,.182,.038),.031,-1.857,.04,1.124,n,.003,9);d.rotation.y=-Math.PI/2;let f=[5011607,9657460,5214573,5331812,10909269].map(g=>Ft(g,.12,.49));for(let g=0;g<5;g++){let p=-.758+g*.379;_e(i,.239,.246,.041,.166,-1.677,.175,p,s,.009,9);let _=Nt(i,[[.057,-.036],[.077,-.036],[.095,-.012],[.105,.009],[.101,.019],[.066,.019],[.058,.01],[.057,-.036]],-1.892,.209,p,f[g],36);_.rotation.z=Math.PI/2,We(i,.057,.008,-1.829,.209,p,s,28).rotation.z=Math.PI/2;for(let S of[-.058,.058])vn(i,[[-1.564,p+S],[-1.32,p+S],[-1.227,p+S*.75]],.035,.01,a),Mn(i,-1.228,.035,p+S*.75,.012,n,s)}let m=Ft(2173751,.21,.46),b=Ft(12831953,.67,.39);for(let g=0;g<9;g++){let p=.538+g%3*.282,_=-.69+Math.floor(g/3)*.41,S=g%3===0?.104:.074,x=g%3===0?.245:.159;Nt(i,[[0,0],[S*.86,0],[S,.011],[S,x-.021],[S*.93,x-.008],[0,x-.008]],p,.033,_,m,28),Nt(i,[[0,x-.01],[S*.9,x-.01],[S*.87,x+.001],[0,x+.001]],p,.033,_,b,24);for(let M of[0,Math.PI/2]){let E=he(i,S*1.24,.0013,.009,p,.035+x,_,s);E.rotation.y=M}he(i,.017,x*.67,S*.34,p+S*.9,.048+x*.43,_,o,.0015)}_e(i,.778,.701,.05,.095,-.391,.091,-.196,s,.009,10);for(let g of[-1,1])for(let p=0;p<15;p++){let _=-.291+p*.0417;he(i,.018,.013,.113,-.391+_,.043,-.196+g*.378,n,.002),he(i,.113,.013,.018,-.391+g*.409,.043,-.196+_,n,.002),vn(i,[[-.391+_,-.196+g*.433],[-.391+_*.95,-.196+g*.532],[-.34+_*.95,-.196+g*.58]],.032,.008,a)}for(let g=0;g<20;g++){let p=-1.214+g*.041;if(!(p>-1.02&&p<-.882)){for(let _ of[-1,1])_e(i,.028,.117,.004,.003,p,_*.036,.881,r,0,4);vn(i,[[p,.824],[p,.76],[p+.063,.698]],.036,.006,a)}}for(let g=0;g<10;g++){let p=-.747+g*.15;vn(i,[[.171,p],[.322,p],[.38,p+.058],[.493,p+.058]],.036,.008,a),Mn(i,.17,.036,p,.012,r,s)}Vi(i,-.327,.056,.319,8,.163,s,n),Vi(i,-1.18,.054,.61,14,.177,s,n);for(let g of[-1.58,1.58])for(let p of[-.7,.7])Nt(i,[[.039,0],[.064,0],[.064,.003],[.039,.003],[.039,0]],g,.034,p,r,20);for(let g=0;g<5;g++){let p=-.758+g*.379;for(let _ of[-.065,.065])Ds(i,-1.686,-.033,p+_,.035,n,r),vn(i,[[-1.686,p+_],[-1.46,p+_],[-1.38,p+_*.55],[-1.2,p+_*.55]],-.033,.009,a),Ds(i,-1.2,-.034,p+_*.55,.016,n,r)}for(let g=0;g<9;g++){let p=.538+g%3*.282,_=-.69+Math.floor(g/3)*.41;for(let S of[-.036,.036])Ds(i,p+S,-.033,_,.022,n,r)}for(let g=0;g<3;g++){let p=-.535+g*.303;for(let _=0;_<9;_++){let S=-.806+_*.139;he(i,.065,.022,.031,S,-.042,p,s,.003);for(let x of[-.034,.034])he(i,.015,.011,.033,S+x,-.038,p,n,.001);vn(i,[[S+.037,p],[S+.052,p-.041],[S+.052,p-.107]],-.033,.007,a),Ds(i,S+.052,-.034,p-.107,.011,n,r)}}_e(i,.353,.198,.019,.047,1.335,.062,.583,s,.003,6);for(let g=0;g<10;g++)g!==8&&he(i,.026,.084,.026,1.21+g%5*.061,.12,.544+Math.floor(g/5)*.078,r,.0015);Pn(i,"F_AUDIO",.335,.092,1.325,.038,.744,"#a6b6ac"),Pn(i,"AUDIO CODEC",.689,.16,-.391,.151,-.196,"#b4c1ce")}function lv(i,e){let{pcb:t,metal:n,black:s,gold:r,traceInk:a}=e;Zy(i,[[-2,-.535],[2,-.535],[2,.471],[1.93,.471],[1.93,.553],[.392,.553],[.392,.433],[.248,.433],[.248,.553],[-1.93,.553],[-1.93,.471],[-2,.471]],.058,t);for(let o of[-1,1]){for(let c=0;c<8;c++){let l=-1.665+c*.475;if(_e(i,.341,.493,.035,.068,l,o*.063,-.099,s,.004,8),o===1)for(let h=0;h<5;h++){let u=l-.12+h*.059;vn(i,[[u,.169],[u,.229],[u-.016,.258],[u-.016,.349]],.031,.009,a),Mn(i,u-.016,.032,.347,.008,r,s)}}for(let c=0;c<71;c++){let l=-1.873+c*.0535;l>.225&&l<.413||_e(i,.036,.148,.006,.003,l,o*.031,.469,r,0,4)}}Vi(i,-1.83,.047,-.438,19,.199,s,n),_e(i,.159,.102,.018,.049,-.02,.055,.284,s,.003,6);for(let o of[-1.58,-.62,.31,1.28]){for(let c=0;c<4;c++)Mn(i,o+c*.038,.032,-.368,.01,r,s);Pn(i,"DRAM",.211,.078,o-.083,.103,-.08,"#a2b0b5")}Pn(i,"DIMM",.312,.105,.059,.034,-.344,"#a2bcb3")}function hv(i,e){let{pcb:t,metal:n,black:s,gold:r,traceInk:a,silk:o}=e,c=new Jt;c.moveTo(-1.95,-.536),c.lineTo(1.95,-.536),c.lineTo(1.95,.263),c.lineTo(1.723,.263),c.lineTo(1.723,.352),c.lineTo(1.95,.352),c.lineTo(1.95,.536),c.lineTo(-1.95,.536),c.lineTo(-1.95,.123),c.quadraticCurveTo(-1.807,.109,-1.807,0),c.quadraticCurveTo(-1.807,-.109,-1.95,-.123),c.closePath(),Re(i,c,.066,0,0,0,t,.002,12).rotation.x=Math.PI/2;for(let h=0;h<24;h++){let u=-.496+h*.043;if(!(u>.238&&u<.377)){for(let d of[-1,1])_e(i,.217,.028,.006,.003,1.832,d*.034,u,r,0,4);vn(i,[[1.728,u],[1.654,u],[1.556,u*.93]],.035,.006,a)}}for(let[h,u,d]of[[-1.261,.68,.744],[-.461,.63,.731],[.291,.536,.591],[1.093,.594,.686]]){_e(i,u,d,.05,.081,h,.077,-.024,s,.005,9);for(let f of[-.364,.346])for(let m=0;m<8;m++)Mn(i,h-u*.43+m*u*.123,.036,f,.008,n,s)}let l=new Jt;l.moveTo(0,-.16),l.absarc(0,0,.16,-Math.PI/2,Math.PI/2,!1),l.lineTo(0,.121),l.absarc(0,0,.121,Math.PI/2,-Math.PI/2,!0),l.closePath(),Re(i,l,.002,-1.949,.035,0,r,0,14).rotation.x=-Math.PI/2;for(let h of[-.455,.431])Vi(i,-1.59,.051,h,16,.209,s,n);he(i,.31,.003,.017,.96,.036,.383,o),Pn(i,"NVMe",.57,.158,1.092,.124,-.018,"#a4b6c3");for(let[h,u]of[[-1.261,"NAND"],[-.461,"NAND"],[.291,"CACHE"]])Pn(i,u,.346,.09,h,.125,-.021,"#8797a2");for(let h=0;h<7;h++)vn(i,[[.47+h*.055,.385],[.515+h*.052,.41],[.82+h*.049,.41]],.035,.008,a)}function uv(i,e){let{pcb:t,metal:n,black:s,gold:r,blue:a}=e,o=Ft(3424078,.44,.46),c=Ft(5665162,.55,.36);_e(i,3.02,1.52,.069,.061,-.45,.026,0,t,.006,10);let l=$(3.9,1.51,.092);for(let f=0;f<8;f++)l.holes.push($(.035,.399,.016,1.209+f*.079,0,new Qe));Re(i,l,.044,0,-.032,0,o,0,5).rotation.x=-Math.PI/2;for(let f=0;f<47;f++)he(i,.026,.218,1.392,-1.8+f*.078,.167,0,n);let h=$(3.872,1.553,.106);for(let f of[-.963,.963])Vt(h,.585,f,0);Re(i,h,.052,0,.28,0,s,.008,15).rotation.x=-Math.PI/2;for(let f of[-.711,.711])_e(i,3.75,.081,.033,.187,0,.213,f,o,.008,10);let u=$(1.78,.563,.036);for(let f=0;f<4;f++)u.holes.push($(.266,.107,.018,-.52+f*.35,.087,new Qe));for(let f=0;f<10;f++)u.holes.push($(.055,.101,.024,-.648+f*.141,-.123,new Qe));Re(i,u,.049,-2.008,.178,0,n,.002,7).rotation.y=-Math.PI/2;for(let f=0;f<4;f++)he(i,.03,.09,.24,-1.967,.265,-.52+f*.35,s,.004),he(i,.035,.014,.151,-1.991,.258,-.52+f*.35,a,.002);for(let f=0;f<27;f++)_e(i,.053,.154,.006,.004,-1.414+f*.078,.056,.803,r);let d=rr(.527,.03,9);for(let f of[-.963,.963]){Nt(i,[[.552,0],[.592,0],[.611,.022],[.608,.042],[.556,.043],[.552,0]],f,.313,0,c,56);let m=new at;m.position.set(f,.363,0),m.rotation.x=-Math.PI/2,i.add(m),m.userData.focusSpin=-8,m.add(new ke(d.clone(),o)),Nt(i,[[0,0],[.125,0],[.143,.015],[.143,.058],[.125,.069],[0,.069]],f,.347,0,s,42),We(i,.087,.006,f,.419,0,c,34)}for(let f of[-1.76,1.76])for(let m of[-.64,.64])$y(i,f,.311,m,.035,n,s);for(let f of[-.718,.718])for(let m=0;m<11;m++)he(i,.015,.032,.005,-1.68+m*.087,.264,f,n);_e(i,.395,.192,.026,.078,1.11,.128,-.773,o,.004,7);for(let f=0;f<4;f++)for(let m=0;m<2;m++)he(i,.037,.031,.004,.973+f*.087,.108+m*.051,-.873,r);d.dispose()}function dv(i,e){let{pcb:t,metal:n,black:s,gold:r,blue:a,traceInk:o}=e,c=$(3.47,3.42,.061);for(let h of[-1.56,1.56])for(let u of[-1.53,1.53])Vt(c,.049,h,u);Re(i,c,.07,0,0,0,t,.003,9).rotation.x=-Math.PI/2,_e(i,1.001,1.063,.091,.067,-.411,.074,-.601,s,.009,10);let l=$(.941,.992,.052);l.holes.push($(.66,.72,.024,0,0,new Qe)),Re(i,l,.045,-.411,.124,-.601,n,.004,10).rotation.x=-Math.PI/2;for(let h=0;h<14;h++)for(let u=0;u<15;u++)he(i,.027,.002,.029,-.69+h*.0427,.11,-.89+u*.041,r);for(let h=0;h<4;h++){let u=.43+h*.25;_e(i,.141,1.89,.038,.092,u,.084,-.508,s,.004,7),he(i,.045,.003,1.6,u,.132,-.508,o);for(let d of[-1.411,.385])_e(i,.132,.11,.02,.06,u,.134,d,h%2?a:n,.004,7)}for(let h=0;h<3;h++){let u=.539+h*.458;_e(i,2.28,.144,.02,.083,-.327,.082,u,s,.004,6),he(i,2.043,.003,.038,-.359,.125,u,r),he(i,.088,.028,.133,.837,.119,u,n,.004)}for(let h of[-1.246,-.809,-.366])_e(i,.383,.352,.044,.258,-1.535,.174,h,n,.008,8),he(i,.013,.12,.228,-1.738,.191,h,s,.002);_e(i,1.403,.304,.042,.133,-.656,.104,-1.368,n,.009,9);for(let h=0;h<25;h++)he(i,.021,.07,.277,-1.308+h*.054,.186,-1.368,n);_e(i,.621,.601,.066,.133,1.047,.105,.998,a,.009,10);for(let h=0;h<11;h++)he(i,.022,.056,.541,.791+h*.051,.182,.998,n,.002);Vi(i,-1.34,.057,1.521,15,.178,s,n);for(let h of[-1.56,1.56])for(let u of[-1.53,1.53])Nt(i,[[.043,0],[.069,0],[.069,.004],[.043,.004],[.043,0]],h,.035,u,r,24);for(let h=0;h<15;h++){let u=-.581+h*.047;vn(i,[[.064,u],[.224,u],[.293,u+.059],[.433,u+.059]],.037,.008,o),Mn(i,.219,.038,u,.013,r,s)}We(i,.166,.042,.673,.059,.344,n,36),We(i,.131,.003,.673,.082,.344,s,30),Pn(i,"+",.12,.095,.673,.085,.344,"#bec7c9")}function b0(i,e,t){let n=new at,s={pcb:Ft(2180160,.18,.62),metal:Ft(10991042,.66,.34),black:Ft(1581098,.13,.62),gold:Ft(12821348,.6,.39),blue:Ft(3562880,.5,.37),traceInk:Ft(4154201,.3,.56),silk:Ft(9741995,.02,.74)};return i==="\u663E\u793A\u5668"||i==="\u7CFB\u7EDF"?tv(n,t,s):i==="BIOS"?nv(n,s):i==="NPU"?iv(n,s):i==="\u5904\u7406\u5668"?rv(n,e,s):i==="\u5185\u5B58"?lv(n,s):i==="\u786C\u76D8"?hv(n,s):i==="\u7535\u6C60"?ov(n,s):i==="\u663E\u5361"?uv(n,s):i==="\u7F51\u5361"?av(n,s):i==="\u58F0\u5361"?cv(n,s):dv(n,s),gn(n),n}function fv(i){let e=new en().setFromObject(i),t=e.getCenter(new P),n=e.getSize(new P);i.position.sub(t);let s=new at;s.add(i),s.scale.setScalar(4.1/Math.max(n.x,n.y,n.z));let r=new at;return r.add(s),r}function pv(i){let e;if(i.traverse(a=>{a.isMesh&&a.name.includes("Cardea_Diff")&&(e=a)}),!e)return;i.updateMatrixWorld(!0);let t=new en().setFromObject(e),n=new ss,s=t.max.z-.044,r=Ft(13215323,.56,.4);for(let a=0;a<62;a++)for(let o of[-1,1]){let c=Ht.lerp(t.min.x+.11,t.max.x-.11,a/61);n.ray.origin.set(c,o,s),n.ray.direction.set(0,-o,0);let l=n.intersectObject(e,!1)[0];l&&he(i,.038,.004,.068,c,l.point.y+o*.003,s,r)}gn(i)}async function _0(i,e,t){let n=`${i}:${e||""}:${["\u663E\u793A\u5668","\u7CFB\u7EDF"].includes(i)?t?.uuid||"default":""}`;return bd.has(n)||bd.set(n,(async()=>{let s=i==="\u5904\u7406\u5668"?e==="intel"?"cpu":null:{\u5185\u5B58:"ram",\u663E\u5361:"gpu",\u786C\u76D8:"storage",\u4E3B\u677F:"motherboard"}[i],r,a="procedural";if(s)try{if(r=(await Jy.loadAsync(`models/${s}.glb`)).scene,i==="\u663E\u5361"){r.rotateZ(Math.PI/2);for(let[u,d]of[["fan",-8],["fan_2",8]]){let f=r.getObjectByName(u);f&&(f.userData.focusSpin=d)}}i==="\u5185\u5B58"&&(r.rotateX(Math.PI/2),r.rotateY(Math.PI/2),r.rotateOnWorldAxis(new P(0,1,0),Math.PI));let c=!1,l=new Set;r.traverse(u=>{if(u.isMesh)for(let d of Array.isArray(u.material)?u.material:[u.material])l.add(d)});let h={cpu:.38,gpu:.43,ram:.38,storage:.54,motherboard:.38}[s];for(let u of l)u.map&&(c=!0,u.map.anisotropy=4),u.roughness=Math.max(h,u.roughness),u.envMapIntensity=s==="storage"?.45:.68,u.emissive&&!u.emissiveMap&&u.emissive.setHex(0),s==="ram"&&u.name==="Case"&&(u.color.setHex(3423819),u.metalness=.25,u.roughness=.62),s==="ram"&&u.name==="Cardea_Diff"&&u.color.setHex(3298124),s==="ram"&&u.name==="ASUS_Emissive_WhiteMaterial"&&(u.color.setHex(11916516),u.emissive.setHex(3364975),u.emissiveIntensity=.28);if(["cpu","gpu","storage"].includes(s)&&!c)throw new Error("No component textures loaded: "+s);a=s}catch(c){console.warn("Hardware component fallback",s,c),r=b0(i,e,t)}else r=b0(i,e,t);let o=fv(r);if(a==="ram"&&pv(o),a==="cpu"&&e==="intel"){let c=new en().setFromObject(o);Pn(o,"intel",.87,.27,0,c.max.y+.012,-.12,"#314253")}return o.userData={source:a,brand:e||"generic"},o})()),(await bd.get(n)).clone(!0)}var y0=i=>Math.max(0,Math.min(1,i)),sa=i=>(i=y0(i),i*i*i*(10+i*(-15+6*i))),Gl=class{constructor(e){Object.assign(this,e),this.current=null,this.pending=null,this.generation=0,this.dragging=null,this.canvas.addEventListener("pointerdown",t=>{if(this.pending&&!this.current){this.post({type:"dismissFocus",requestId:this.requestId});return}let n=this.current;!n||n.closing||(this.dragging={x:t.clientX,y:t.clientY,px:t.clientX,py:t.clientY,moved:!1},n.auto=!1,this.canvas.setPointerCapture(t.pointerId),this.post({type:"focusAutoStopped",requestId:n.requestId}))}),this.canvas.addEventListener("pointermove",t=>{let n=this.dragging;if(!n||!this.current)return;let s=t.clientX-n.px,r=t.clientY-n.py;n.moved||=Math.hypot(t.clientX-n.x,t.clientY-n.y)>4,n.px=t.clientX,n.py=t.clientY,this.drag(s,r)}),this.canvas.addEventListener("pointerup",t=>{let n=this.dragging;this.dragging=null;let s=this.current;if(!(!n||!s||s.closing)&&!n.moved){let r=this.canvas.getBoundingClientRect(),a=new ss;a.setFromCamera(new ae((t.clientX-r.left)/r.width*2-1,1-(t.clientY-r.top)/r.height*2),this.camera),a.intersectObject(s.part,!0).length||this.post({type:"dismissFocus",requestId:s.requestId})}}),this.canvas.addEventListener("pointercancel",()=>this.dragging=null),this.canvas.addEventListener("lostpointercapture",()=>this.dragging=null)}get active(){return!!(this.current||this.pending)}get requestId(){return this.current?.requestId??this.pending?.message.requestId??0}freezeControls(){let e={enabled:this.controls.enabled,damping:this.controls.enableDamping,autoRotate:this.controls.autoRotate},t=this.camera.position.clone(),n=this.camera.quaternion.clone(),s=this.controls.target.clone();return this.controls.enabled=!1,this.controls.autoRotate=!1,this.controls.enableDamping=!1,this.controls.update(),this.camera.position.copy(t),this.camera.quaternion.copy(n),this.controls.target.copy(s),this.camera.updateMatrixWorld(!0),this.controls.enableDamping=e.damping,e}restoreControls(e,t){this.controls.enabled=e.enabled,this.controls.enableDamping=e.damping,this.controls.autoRotate=e.autoRotate,this.restoreAuto(t)}prepare(e,t){if(this.current)return Promise.resolve(null);if(this.pending)return this.pending.promise;let n=this.getModel();if(!n)return Promise.resolve(null);let s={message:e,autoBefore:t,model:n,generation:++this.generation,controls:this.freezeControls()};return this.pending=s,s.promise=(async()=>{let r=await _0(e.label,e.brand,this.getScreenTexture());if(this.pending!==s||s.generation!==this.generation||(await this.prepareObject(r),this.pending!==s||s.generation!==this.generation))return null;let a=new en().setFromObject(r).getBoundingSphere(new mn).radius;return{geometry:r,radius:a}})(),s.promise}async start(e,t){if(this.current)return;let n=this.prepare(e,t),s=this.pending,r=await n;if(!r||!s||this.pending!==s||s.generation!==this.generation||this.getModel()!==s.model)return;this.pending=null;let a=s.model,o=e.side==="left"?.75:.25,c=a.position.clone(),l=a.quaternion.clone(),h=a.scale.clone(),u=new at;u.add(r.geometry),u.scale.setScalar(1e-4),this.scene.add(u);let d=[];r.geometry.traverse(b=>{b.userData.focusSpin&&d.push(b)});let f=this.point(.5,.5),m=this.current={label:e.label,requestId:e.requestId??0,model:a,part:u,position:c,rotation:l,scale:h,targetRotation:l.clone().multiply(new ln().setFromAxisAngle(new P(0,1,0),e.side==="left"?-.3:.3)),horizontal:this.point(o,.5).sub(f),downward:this.point(.5,.78).sub(f),destination:this.point(o,.29),up:new P(0,1,0).applyQuaternion(this.camera.quaternion),origin:new P,controls:s.controls,autoBefore:s.autoBefore,auto:!this.reduced(),fans:d,yaw:["\u663E\u793A\u5668","\u7CFB\u7EDF"].includes(e.label)?-.18:.28,tilt:0,pitch:["\u663E\u793A\u5668","\u7CFB\u7EDF"].includes(e.label)?0:1.02,euler:new si,orientation:new ln,progress:0,closing:!1,duration:this.reduced()?0:1040,fit:Math.min(1.4,this.point(.69,.3).distanceTo(this.point(.31,.3))/(2*r.radius),this.point(.5,.08).distanceTo(this.point(.5,.55))/(2*r.radius))};m.start=performance.now(),this.post({type:"focusStarted",requestId:m.requestId}),this.requestFrame()}point(e,t){let n=this.controls.target.clone().project(this.camera);return new P(e*2-1,1-t*2,n.z).unproject(this.camera)}markerPosition(e){let t=this.getMarker(e.label);return t?t.getWorldPosition(e.origin):e.origin.copy(e.model.position)}update(e,t){let n=this.current;if(!n)return!1;let s=n.duration?y0((e-n.start)/n.duration):1,r=sa(s);if(n.closing)return n.progress=n.closeProgress*(1-r),n.model.position.lerpVectors(n.closePosition,n.position,r),n.model.quaternion.slerpQuaternions(n.closeRotation,n.rotation,r),n.model.scale.lerpVectors(n.closeScale,n.scale,r),n.part.position.lerpVectors(n.closePartPosition,this.markerPosition(n),r),n.part.scale.setScalar(Math.max(1e-4,n.closePartScale*(1-r))),s===1?(this.finish(n),!1):!0;n.progress=r,n.model.position.copy(n.position).addScaledVector(n.horizontal,sa(s/.68)).addScaledVector(n.downward,sa((s-.2)/.8)),n.model.quaternion.copy(n.rotation).slerp(n.targetRotation,sa(s/.9)),n.model.scale.copy(n.scale).multiplyScalar(1-.22*r);let a=sa((s-.42)/.58);n.part.position.copy(this.markerPosition(n)).lerp(n.destination,a),n.part.position.addScaledVector(n.up,Math.sin(a*Math.PI)*.22),n.part.scale.setScalar(Math.max(1e-4,a*n.fit)),n.auto&&(n.yaw+=t*.22*sa((a-.35)/.65)),n.part.quaternion.copy(this.camera.quaternion).multiply(n.orientation.setFromEuler(n.euler.set(n.pitch+n.tilt,n.yaw,0)));let o=n.fans.length>0&&!this.reduced();if(o)for(let c of n.fans)c.rotateZ(c.userData.focusSpin*t);return s<1||n.auto||o}drag(e,t){let n=this.current;!n||n.closing||(n.auto&&this.post({type:"focusAutoStopped",requestId:n.requestId}),n.auto=!1,n.yaw+=e*.009,n.tilt=Ht.clamp(n.tilt+t*.008,-1.2,1.2),this.requestFrame())}close(e=!1,t){if(t!==void 0&&t!==this.requestId)return;if(this.dragging=null,this.pending){let s=this.pending;this.pending=null,this.generation++,this.restoreControls(s.controls,s.autoBefore),this.post({type:"focusClosed",requestId:s.message.requestId??0}),this.requestFrame();return}let n=this.current;if(n){if(e){this.finish(n),this.requestFrame();return}n.closing||(this.generation++,n.closePosition=n.model.position.clone(),n.closeRotation=n.model.quaternion.clone(),n.closeScale=n.model.scale.clone(),n.closePartPosition=n.part.position.clone(),n.closePartScale=n.part.scale.x,n.closeProgress=n.progress,n.closing=!0,n.auto=!1,n.duration=this.reduced()?0:680,n.start=performance.now(),this.requestFrame())}}finish(e){this.scene.remove(e.part),e.model.position.copy(e.position),e.model.quaternion.copy(e.rotation),e.model.scale.copy(e.scale),this.current=null,this.restoreControls(e.controls,e.autoBefore),this.post({type:"focusClosed",requestId:e.requestId})}state(){if(!this.current)return this.pending?{label:this.pending.message.label,preparing:!0}:null;let e=this.current,t=[];return e.part.traverse(n=>{if(n.isMesh)for(let s of Array.isArray(n.material)?n.material:[n.material])t.some(r=>r.name===s.name)||t.push({name:s.name,type:s.type,color:s.color?.getHexString(),metalness:s.metalness,roughness:s.roughness,map:s.map?.image?{width:s.map.image.width,height:s.map.image.height}:null})}),{label:e.label,requestId:e.requestId,progress:e.progress,autoRotate:e.auto,closing:e.closing,partVisible:!0,asset:e.part.children[0]?.userData,rotation:e.part.rotation.toArray(),modelPosition:e.model.position.toArray(),fanPose:e.fans.map(n=>n.quaternion.toArray()),materials:t}}};var Vl=class{constructor(e,t,n){this.getScreen=e,this.studio=t,this.fill=n,this.active=!1,this.overlay=document.createElement("canvas"),this.overlay.setAttribute("aria-hidden","true"),this.overlay.style.cssText="position:absolute;inset:0;width:100%;height:100%;pointer-events:none;display:none",document.body.append(this.overlay)}setActive(e){if(this.active!==e)if(this.active=e,e){this.started=performance.now(),this.lastScreen=0,this.screenFrames=0,this.samples=[],this.sample={cpuTemp:-1,gpuTemp:-1,cpuLoad:-1,gpuLoad:-1},this.canvas=document.createElement("canvas"),this.canvas.width=1024,this.canvas.height=640,this.texture=new xn(this.canvas),this.texture.colorSpace=pt;let t=this.getScreen();this.original=t.map,t.map=this.texture,t.needsUpdate=!0,this.studioColor=this.studio.color.clone(),this.fillColor=this.fill.color.clone(),this.studio.color.set(16766368),this.fill.color.set(16754272),this.overlay.style.display="block"}else{let t=this.getScreen();t?.map===this.texture&&(t.map=this.original,t.needsUpdate=!0),this.texture?.dispose(),this.texture=null,this.original=null,this.canvas=null,this.studio.color.copy(this.studioColor),this.fill.color.copy(this.fillColor),this.overlay.style.display="none"}}setSample(e){this.active&&(this.sample=e,this.samples.push({cpu:e.cpuLoad,gpu:e.gpuLoad}),this.samples.length>60&&this.samples.shift(),this.lastScreen=0)}update(e,t,n){if(!this.active)return;let s=(e-this.started)/1e3;e-this.lastScreen>250&&(this.paintScreen(s,n),this.lastScreen=e);let r=Math.max(1,Math.round(innerWidth)),a=Math.max(1,Math.round(innerHeight));(this.overlay.width!==r||this.overlay.height!==a)&&(this.overlay.width=r,this.overlay.height=a);let o=this.overlay.getContext("2d");o.clearRect(0,0,r,a);let c=t.\u6563\u70ED||[.5,.7],l=Math.max(.15,Math.min(.85,c[0]))*r,h=Math.max(.35,Math.min(.92,c[1]))*a,u=.45+.25*Math.min(1,s/15),d=o.createRadialGradient(l,h,0,l,h,r*.8);if(d.addColorStop(0,`rgba(255,139,42,${u*.16})`),d.addColorStop(1,"rgba(255,190,80,0)"),o.fillStyle=d,o.fillRect(0,0,r,a),!n)for(let f=0;f<8;f++){let m=(s*.26+f*.137)%1,b=(f-3.5)*r*.047,g=m*a*.42,p=l+b+Math.sin(s*1.4+f)*8;o.beginPath(),o.moveTo(p,h-g),o.bezierCurveTo(p-14,h-g-20,p+16,h-g-48,p+Math.sin(s+f)*10,h-g-76),o.strokeStyle=`rgba(255,205,131,${Math.sin(m*Math.PI)*u*.3})`,o.lineWidth=1.3+f%3*.5,o.stroke()}}paintScreen(e,t){let n=this.canvas.getContext("2d"),s=1024,r=640,a=n.createLinearGradient(0,0,s,r);a.addColorStop(0,"#341c20"),a.addColorStop(.5,"#7f321e"),a.addColorStop(1,"#d77832"),n.fillStyle=a,n.fillRect(0,0,s,r);let o=800+(t?0:Math.sin(e*.2)*24),c=n.createRadialGradient(o,160,8,o,160,300);c.addColorStop(0,"#ffdc80"),c.addColorStop(.22,"#ffc16c88"),c.addColorStop(1,"#ffaf4300"),n.fillStyle=c,n.fillRect(0,0,s,r),n.fillStyle="#ffe9c8",n.font="500 24px Segoe UI",n.fillText("TUBA / STABILITY CHECK",56,62),n.font="600 56px Segoe UI",n.fillText("\u70E4\u673A\u8FDB\u884C\u4E2D",56,140),n.font="24px Consolas",n.fillText(`${Math.floor(e/60).toString().padStart(2,"0")}:${Math.floor(e%60).toString().padStart(2,"0")}`,56,188);let l=(u,d)=>Number.isFinite(u)&&u>=0?`${Math.round(u)}${d}`:"--",h=[["CPU \u6E29\u5EA6",l(this.sample.cpuTemp,"\xB0C")],["GPU \u6E29\u5EA6",l(this.sample.gpuTemp,"\xB0C")],["CPU \u8D1F\u8F7D",l(this.sample.cpuLoad,"%")],["GPU \u8D1F\u8F7D",l(this.sample.gpuLoad,"%")]];for(let u=0;u<h.length;u++){let d=56+u%2*440,f=260+Math.floor(u/2)*110;n.fillStyle="#f9cda9",n.font="22px Segoe UI",n.fillText(h[u][0],d,f),n.fillStyle="#fff0d6",n.font="600 44px Segoe UI",n.fillText(h[u][1],d,f+48)}n.strokeStyle="#ffd69666",n.lineWidth=1,n.beginPath(),n.moveTo(56,580),n.lineTo(960,580),n.stroke();for(let[u,d]of[["cpu","#ffdc91"],["gpu","#ffa17c"]]){n.strokeStyle=d,n.lineWidth=3,n.beginPath();let f=!1;for(let m=0;m<this.samples.length;m++){let b=this.samples[m][u];if(!(b>=0)){f=!1;continue}let g=56+m*904/59,p=574-Math.min(100,b)*.7;f?n.lineTo(g,p):n.moveTo(g,p),f=!0}n.stroke()}n.fillStyle="#ffe1a0",n.fillRect(56+(t?0:e*.12%1)*824,606,80,3),this.texture.needsUpdate=!0,this.screenFrames++}state(){return{active:this.active,screenFrames:this.screenFrames??0,sample:this.sample??null}}};var Wl=class extends Xs{constructor(){super(),this.name="RoomEnvironment",this.position.y=-3.5;let e=new tn;e.deleteAttribute("uv");let t=new mt({side:yn}),n=new mt,s=new Qs(16777215,900,28,2);s.position.set(.418,16.199,.3),this.add(s);let r=new ke(e,t);r.position.set(-.757,13.219,.717),r.scale.set(31.713,28.305,28.591),this.add(r);let a=new ri(e,n,6),o=new At;o.position.set(-10.906,2.009,1.846),o.rotation.set(0,-.195,0),o.scale.set(2.328,7.905,4.651),o.updateMatrix(),a.setMatrixAt(0,o.matrix),o.position.set(-5.607,-.754,-.758),o.rotation.set(0,.994,0),o.scale.set(1.97,1.534,3.955),o.updateMatrix(),a.setMatrixAt(1,o.matrix),o.position.set(6.167,.857,7.803),o.rotation.set(0,.561,0),o.scale.set(3.927,6.285,3.687),o.updateMatrix(),a.setMatrixAt(2,o.matrix),o.position.set(-2.017,.018,6.124),o.rotation.set(0,.333,0),o.scale.set(2.002,4.566,2.064),o.updateMatrix(),a.setMatrixAt(3,o.matrix),o.position.set(2.291,-.756,-2.621),o.rotation.set(0,-.286,0),o.scale.set(1.546,1.552,1.496),o.updateMatrix(),a.setMatrixAt(4,o.matrix),o.position.set(-2.193,-.369,-5.547),o.rotation.set(0,.516,0),o.scale.set(3.875,3.487,2.986),o.updateMatrix(),a.setMatrixAt(5,o.matrix),this.add(a);let c=new ke(e,ra(50));c.position.set(-16.116,14.37,8.208),c.scale.set(.1,2.428,2.739),this.add(c);let l=new ke(e,ra(50));l.position.set(-16.109,18.021,-8.207),l.scale.set(.1,2.425,2.751),this.add(l);let h=new ke(e,ra(17));h.position.set(14.904,12.198,-1.832),h.scale.set(.15,4.265,6.331),this.add(h);let u=new ke(e,ra(43));u.position.set(-.462,8.89,14.52),u.scale.set(4.38,5.441,.088),this.add(u);let d=new ke(e,ra(20));d.position.set(3.235,11.486,-12.541),d.scale.set(2.5,2,.1),this.add(d);let f=new ke(e,ra(100));f.position.set(0,20,0),f.scale.set(1,.1,1),this.add(f)}dispose(){let e=new Set;this.traverse(t=>{t.isMesh&&(e.add(t.geometry),e.add(t.material))});for(let t of e)t.dispose()}};function ra(i){return new ai({color:0,emissive:16777215,emissiveIntensity:i})}function v0(i=!1){let e=[],t=(s,r,a,o,c=1,l=1)=>e.push({id:s,label:r,x:a,z:o,w:c,h:l}),n=(s,r)=>{let a=0;for(let[o,c,l=1]of s)t(o,c,a,r,l),a+=l};t(27,"Esc",0,0);for(let s=0;s<12;s++)t(112+s,`F${s+1}`,2+s+Math.floor(s/4)*.5,0);if(n([[192,"~  `"],..."1234567890".split("").map(s=>[s.charCodeAt(0),s]),[189,"\u2212  _"],[187,"+  ="],[8,"Backspace",2]],1.4),n([[9,"Tab",1.5],..."QWERTYUIOP".split("").map(s=>[s.charCodeAt(0),s]),[219,"{  ["],[221,"}  ]"],[220,"|  \\",1.5]],2.4),n([[20,"Caps Lock",1.75],..."ASDFGHJKL".split("").map(s=>[s.charCodeAt(0),s]),[186,":  ;"],[222,`"  '`],[13,"Enter",2.25]],3.4),n([[160,"Shift",2.25],..."ZXCVBNM".split("").map(s=>[s.charCodeAt(0),s]),[188,"<  ,"],[190,">  ."],[191,"?  /"],[161,"Shift",2.75]],4.4),n([[162,"Ctrl",1.25],[91,"Win",1.25],[164,"Alt",1.25],[32,"Space",5],[165,"Alt",1.25],[92,"Win",1.25],[57470,"Copilot",1.25],[93,"Menu",1.25],[163,"Ctrl",1.25]],5.4),[[44,"PrtSc"],[145,"ScrLk"],[19,"Pause"]].forEach(([s,r],a)=>t(s,r,15.5+a,0)),[[45,"Insert"],[36,"Home"],[33,"PgUp"],[46,"Delete"],[35,"End"],[34,"PgDn"]].forEach(([s,r],a)=>t(s,r,15.5+a%3,1.4+Math.floor(a/3))),t(38,"\u2191",16.5,4.4),[[37,"\u2190"],[40,"\u2193"],[39,"\u2192"]].forEach(([s,r],a)=>t(s,r,15.5+a,5.4)),!i){[[144,"Num"],[111,"/"],[106,"\xD7"],[109,"\u2212"]].forEach(([s,r],a)=>t(s,r,19+a,1.4));for(let s=0;s<3;s++)for(let r=0;r<3;r++){let a=7-s*3+r;t(96+a,`${a}`,19+r,2.4+s)}t(107,"+",22,2.4,1,2),t(57372,"Enter",22,4.4,1,2),t(96,"0",19,5.4,2),t(110,".",21,5.4)}return e}function _d(i,e,t,n,s){let r=i-t,a=Math.exp(-s*n),o=e+s*r;return[t+(r+o*n)*a,(e-s*o*n)*a]}var M0=1e3;function S0({renderer:i,invalidate:e,post:t=()=>{},offset:n=0,scale:s=1,floor:r=!0,lighting:a=!0}){let o=new at;o.position.z=n,o.scale.setScalar(s);let c=new Yt(34,1,.1,200);a&&o.add(new $s(16774885,6977404,1.8));let l=new yi(16773851,2.1);l.position.set(-8,18,8),l.castShadow=!0,l.shadow.mapSize.set(2048,2048),l.shadow.camera.left=-18,l.shadow.camera.right=18,l.shadow.camera.top=18,l.shadow.camera.bottom=-18,l.shadow.normalBias=.035,l.shadow.bias=-15e-5,l.shadow.radius=3,a&&o.add(l);let h=new yi(14740726,.45);h.position.set(10,7,-7),a&&o.add(h);let u=new ai({color:14278620}),d=new ke(new Vn(200,200),u);d.rotation.x=-Math.PI/2,d.position.y=-5.2,d.receiveShadow=!0,r&&o.add(d);let f=document.createElement("canvas");f.width=32,f.height=32;let m=f.getContext("2d");m.fillStyle="#a47a50",m.fillRect(0,0,32,32);let b=74,g=()=>(b=b*1664525+1013904223>>>0,b/4294967296);for(let q=0;q<85;q++)m.fillStyle=["#986c44","#b0885b","#ad8254","#936941"][Math.floor(g()*4)],m.fillRect(Math.floor(g()*32),Math.floor(g()*32),2+Math.floor(g()*13),1);m.fillStyle="#805a37",m.fillRect(0,0,32,1),m.fillStyle="#b58a5c",m.fillRect(0,1,32,1);let p=new xn(f);p.magFilter=Wt,p.minFilter=Wt,p.wrapS=p.wrapT=Pi,p.repeat.set(1,3),p.colorSpace=pt;let _=new ai({map:p,color:13022367}),S=new ai({color:7885623}),x=new at;o.add(x);for(let q=0;q<8;q++)qe(new tn(3.985,.65,14),_,x,-14+q*4,-.575,0);qe(new tn(32,.42,.24),S,x,0,-.96,6.9);for(let q of[-15.88,15.88])qe(new tn(.24,.42,14),S,x,q,-.96,0);for(let q of[-14,14])for(let oe of[-5.5,5.5])qe(new tn(1.15,4.3,1.15),S,x,q,-3.05,oe);let M,E,A,y=[],w=!1,R=!1,I=new Be("#1677b9"),L=new Map,F=new Set,D=new Set,k=new Be,W=new Be,X=new Be("#62ac85"),re=new Be("#123d63"),j=new Be("#273439"),ee=new Be("#f7fbff"),te=!0,Ne=0,Se=0,ct=null,$e=!1,ht=!0,J="\u7B49\u5F85\u6309\u952E",ie="\u534A\u4FEF\u89C6 \xB7 \u5168\u666F",ve=matchMedia("(prefers-reduced-motion: reduce)").matches,Ke=matchMedia("(prefers-reduced-motion: reduce)"),Ae=q=>{ve=q.matches,me()};Ke.addEventListener("change",Ae);let ze=[0,17,19,0,0,0],bt=[0,0,0,0,0,0],Q=[...ze],ce=document.createElement("canvas");ce.width=2048,ce.height=1024;let le=new xn(ce);le.colorSpace=pt,le.magFilter=Xt,le.minFilter=qn,le.anisotropy=Math.min(16,i.capabilities.getMaxAnisotropy());let de=new jt({map:le,vertexColors:!0,transparent:!0,toneMapped:!1,depthWrite:!1,polygonOffset:!0,polygonOffsetFactor:-1}),xe=new ss,Xe=new ae,Fe=new At;function qe(q,oe,ge,je,U,fe){let B=new ke(q,oe);return B.position.set(je,U,fe),B.castShadow=!0,B.receiveShadow=!0,ge.add(B),B}function Je(){if(M){o.remove(M);let B=new Set;M.traverse(ue=>{ue.isInstancedMesh&&ue.dispose(),ue.isMesh&&(ue.geometry.dispose(),ue.material!==de&&B.add(ue.material))}),B.forEach(ue=>ue.dispose())}M=new at,o.add(M),L.clear();let q=v0(w),oe=w?18.5:23;M.position.set(-oe/2,0,-3.2);let ge=new ai({color:3423811}),je=new ai({color:1910571});qe(new tn(oe+.5,.35,6.9),ge,M,oe/2,-.045,3.2),qe(new tn(oe+.3,.14,6.7),je,M,oe/2,.18,3.2),qe(new tn(.11,.11,3.4),je,M,oe*.7,-.15,-1.85),E=new ri(new tn(1,.3,1),new ai,q.length),E.castShadow=!0,E.receiveShadow=!0,E.instanceMatrix.setUsage(ho),M.add(E),y=q.map(B=>B.id);let U=[];q.forEach((B,ue)=>{let be=new at;be.position.set(B.x+B.w/2,.33,B.z+B.h/2),Fe.position.set(be.position.x,.48,be.position.z),Fe.scale.set(B.w-.12,1,B.h-.12),Fe.updateMatrix(),E.setMatrixAt(ue,Fe.matrix),E.setColorAt(ue,new Be("#e3e1d5"));let se=new Vn(Math.min(B.w-.24,1.9),Math.min(B.h-.2,.84)),Ie=se.attributes.uv;for(let Pe=0;Pe<Ie.count;Pe++)Ie.setXY(Pe,(ue%16+Ie.getX(Pe))/16,1-(Math.floor(ue/16)+1-Ie.getY(Pe))/8);se.rotateX(-Math.PI/2),se.translate(be.position.x,.632,be.position.z),U.push(se),L.set(B.id,{...B,group:be,depth:0,speed:0,color:new Be("#e3e1d5"),labelIndex:ue})}),A=go(U),U.forEach(B=>B.dispose()),A.attributes.position.setUsage(ho);let fe=new nt(new Float32Array(A.attributes.position.count*3),3);for(let B=0;B<fe.count;B++)fe.setXYZ(B,j.r,j.g,j.b);fe.setUsage(ho),A.setAttribute("color",fe),M.add(new ke(A,de)),E.computeBoundingSphere(),te=!0,xt(),Y(),C()}function N(){let q=ce.getContext("2d");q.clearRect(0,0,2048,1024),q.textAlign="center",q.textBaseline="middle",q.fillStyle="#ffffff";for(let oe of L.values()){let ge=oe.label.length>5?26:oe.label.length>2?36:oe.label.length>1?48:70;q.font=`bold ${ge}px Consolas,monospace`,q.fillText(oe.label,oe.labelIndex%16*128+64,Math.floor(oe.labelIndex/16)*128+64,117),(oe.id===70||oe.id===74)&&(q.globalAlpha=.5,q.fillRect(oe.labelIndex%16*128+51,Math.floor(oe.labelIndex/16)*128+106,26,3),q.globalAlpha=1)}le.needsUpdate=!0}function xt(){u.color.set(R?"#262e32":"#d9dfdc"),N(),me()}function st(){let q=Math.max(22,(w?20:24.6)/(2*Math.tan(Ht.degToRad(c.fov/2))*c.aspect)*1.1);return[q*.24*s,q*.81*s,q*.76*s+n,0,-.6*s,n]}function C(){clearTimeout(Se),Se=0,ct=null,Q=st(),ie="\u534A\u4FEF\u89C6 \xB7 \u5168\u666F",Y(),me()}function v(q,oe=!1){let ge=L.get(q);if(!ge)return;ct=q;let je=(ge.group.position.x+M.position.x)*s,U=(ge.group.position.z+M.position.z)*s+n,fe=Math.max(8.5,6.8/Math.max(.5,c.aspect))*s;Q=[je+1.2*s,fe*.85,U+fe*.87,je,.25*s,U],ie=oe?`${ge.label} \xB7 \u89C6\u89D2\u9884\u89C8`:`${V(ge)} \xB7 \u6309\u952E\u7279\u5199`,Y(),me()}function z(){clearTimeout(Se),Se=0,D.size||(Se=setTimeout(C,M0))}function V(q){return[160,162,164,91].includes(q.id)?`\u5DE6 ${q.label}`:[161,163,165,92].includes(q.id)?`\u53F3 ${q.label}`:q.id===57372?"\u6570\u5B57\u533A Enter":q.id>=96&&q.id<=111?`\u6570\u5B57\u533A ${q.label}`:q.label}function Y(){let q=[...F].filter(ge=>L.has(ge)).length,oe=[...D].map(ge=>L.get(ge)).filter(Boolean);t({type:"stats",count:q,total:L.size,last:J,held:oe.length,view:ie})}function pe(q){if(!$e)switch(q.type){case"key":{let oe=Number(q.key),ge=L.get(oe);q.down?(clearTimeout(Se),D.add(oe),ge?(F.add(oe),J=V(ge),v(oe)):J=`\u6269\u5C55\u952E 0x${oe.toString(16).toUpperCase()}`):(D.delete(oe),z()),Y(),me();break}case"layout":w!==!!q.compact&&(w=!!q.compact,Je());break;case"theme":{R=!!q.dark,/^#[\da-f]{6}$/i.test(q.accent||"")&&I.set(q.accent),xt();break}case"reset":F.clear(),J="\u7B49\u5F85\u6309\u952E",Y(),C();break;case"panorama":C();break}}function me(){$e||e()}function Z(q){if($e)return!1;let oe=Math.min(.05,Ne?(q-Ne)/1e3:1/60);Ne=q;let ge=!1;for(let B=0;B<ze.length;B++)[ze[B],bt[B]]=ve?[Q[B],0]:_d(ze[B],bt[B],Q[B],oe,9),(Math.abs(ze[B]-Q[B])>1e-4||Math.abs(bt[B])>1e-4)&&(ge=!0);c.position.set(...ze.slice(0,3)),c.lookAt(...ze.slice(3));let je=!1,U=!1,fe=!1;for(let B of L.values()){let ue=B.depth,be=D.has(B.id),se=be?-.17:0;if(B.wasDown!==be){B.wasDown=be;let Ie=be?ee:j;for(let Pe=0;Pe<4;Pe++)A.attributes.color.setXYZ(B.labelIndex*4+Pe,Ie.r,Ie.g,Ie.b);fe=!0}if([B.depth,B.speed]=ve?[se,0]:_d(B.depth,B.speed,se,oe,be?32:23),B.group.position.y=.33+B.depth,B.depth!==ue){Fe.position.set(B.group.position.x,.48+B.depth,B.group.position.z),Fe.scale.set(B.w-.12,1,B.h-.12),Fe.updateMatrix(),E.setMatrixAt(B.labelIndex,Fe.matrix),je=!0;for(let Ie=0;Ie<4;Ie++)A.attributes.position.setY(B.labelIndex*4+Ie,.632+B.depth)}(Math.abs(B.depth-se)>1e-4||Math.abs(B.speed)>1e-4)&&(ge=!0,te=!0),k.set(B.id===27?"#c98a68":B.label.length>1?"#b6bdba":"#e3e1d5"),F.has(B.id)&&k.lerp(X,.68),W.copy(I).lerp(re,.12),be&&k.copy(W),Math.abs(B.color.r-k.r)+Math.abs(B.color.g-k.g)+Math.abs(B.color.b-k.b)>.001&&(B.color.lerp(k,ve?1:1-Math.exp(-oe*18)),E.setColorAt(B.labelIndex,B.color),U=!0,ge=!0)}return je&&(E.instanceMatrix.needsUpdate=!0,A.attributes.position.needsUpdate=!0),U&&(E.instanceColor.needsUpdate=!0),fe&&(A.attributes.color.needsUpdate=!0),te&&(i.shadowMap.needsUpdate=!0,te=!1),ge}function ne(q,oe){c.aspect=q/Math.max(oe,1),c.updateProjectionMatrix(),ct===null?Q=st():v(ct),me()}function ye(q){if(!ht||$e)return;Xe.set(q.clientX/innerWidth*2-1,-q.clientY/innerHeight*2+1),xe.setFromCamera(Xe,c),o.updateMatrixWorld(!0);let oe=xe.intersectObject(E)[0];oe&&(v(y[oe.instanceId],!0),z())}i.domElement.addEventListener("pointerdown",ye);function He(q){ht=q,D.clear(),clearTimeout(Se),Se=0,Ne=0,q&&C()}function Me(){if($e)return;$e=!0,clearTimeout(Se),i.domElement.removeEventListener("pointerdown",ye),Ke.removeEventListener("change",Ae);let q=new Set,oe=new Set;o.traverse(ge=>{ge.isInstancedMesh&&ge.dispose(),ge.isMesh&&(q.add(ge.geometry),oe.add(ge.material))}),q.forEach(ge=>ge.dispose()),oe.forEach(ge=>ge.dispose()),r||(d.geometry.dispose(),u.dispose()),p.dispose(),le.dispose(),l.shadow.dispose()}return Je(),ne(innerWidth,innerHeight),Q=st(),ze.splice(0,6,...Q),Z(performance.now()),{scene:o,camera:c,update:Z,resize:ne,receive:pe,setActive:He,dispose:Me,get targetPoint(){return new P(...ze.slice(3))},setLightWeight(q){o.traverse(oe=>{oe.isLight&&(oe.userData.baseIntensity??=oe.intensity,oe.intensity=oe.userData.baseIntensity*q)})},getState:()=>({floor:r,lighting:a,compact:w,held:[...D],visited:[...F],focused:ct,pose:[...ze],target:[...Q],keyCount:L.size})}}var ql=class{constructor(){this.canvas=document.createElement("canvas"),this.canvas.setAttribute("aria-hidden","true"),this.canvas.style.cssText="position:absolute;inset:0;width:100%;height:100%;pointer-events:none;display:none",document.body.append(this.canvas),this.active=!1,this.running=!1,this.particles=[]}start({reduced:e=!1,dark:t=!1}={}){if(this.active=!0,this.reduced=e,this.dark=t,this.started=performance.now(),this.running=!e,this.canvas.style.display="block",this.particles=[],e)return;let n=731,s=()=>(n=1664525*n+1013904223>>>0,n/4294967296),r=["#72cdb0","#e6c174","#d98d87","#a99bce","#ece5d6"];for(let a=0;a<88;a++){let o=a%2?1:-1;this.particles.push({side:o,delay:s()*.42,vx:-o*(85+s()*150),vy:-90-s()*150,size:4+s()*4,spin:(s()-.5)*9,angle:s()*Math.PI,color:r[a%r.length],sway:s()*6.28})}}stop(){this.active=this.running=!1,this.particles=[],this.canvas.style.display="none"}update(e){if(!this.active)return!1;let t=Math.max(1,innerWidth),n=Math.max(1,innerHeight),s=Math.min(2,devicePixelRatio||1);(this.canvas.width!==Math.round(t*s)||this.canvas.height!==Math.round(n*s))&&(this.canvas.width=Math.round(t*s),this.canvas.height=Math.round(n*s));let r=this.canvas.getContext("2d");r.setTransform(s,0,0,s,0,0),r.clearRect(0,0,t,n);let a=Math.max(0,(e-this.started)/1e3),o=this.reduced?1:Math.min(1,a/.9);r.save(),r.translate(t*.5,n*.89),r.scale(1,.16);let c=Math.min(t*.28,n*.78),l=r.createRadialGradient(0,0,c*.25,0,0,c);if(l.addColorStop(0,"rgba(115,199,165,0)"),l.addColorStop(.65,`rgba(115,199,165,${o*(this.dark?.13:.09)})`),l.addColorStop(1,"rgba(115,199,165,0)"),r.fillStyle=l,r.beginPath(),r.arc(0,0,c,0,Math.PI*2),r.fill(),!this.reduced&&a<1.6){let h=a/1.6;r.strokeStyle=`rgba(151,211,184,${(1-h)*.25})`,r.lineWidth=1,r.beginPath(),r.arc(0,0,c*(.6+.4*h),0,Math.PI*2),r.stroke()}if(r.restore(),!this.reduced&&a<3.8)for(let h of this.particles){let u=a-h.delay;if(u<0)continue;let d=t*(h.side>0?.88:.12)+h.vx*(1-Math.exp(-u*.55))/.55+Math.sin(u*3+h.sway)*12,f=n*.16+h.vy*u+165*u*u;f>n+20||(r.save(),r.globalAlpha=Math.min(1,Math.max(0,(3.8-a)/.8)),r.translate(d,f),r.rotate(h.angle+h.spin*u),r.scale(.35+.65*Math.abs(Math.cos(h.sway+u*5)),1),r.fillStyle=h.color,r.fillRect(-h.size/2,-h.size*.3,h.size,h.size*.6),r.restore())}return this.running=!this.reduced&&a<3.8,this.running||(this.particles=[]),this.running}state(){return{active:this.active,running:this.running,reduced:!!this.reduced,particles:this.particles.length}}dispose(){this.stop(),this.canvas.remove()}};var ft=new Rl({antialias:!0,alpha:!0,preserveDrawingBuffer:!0});ft.setPixelRatio(Math.min(devicePixelRatio,2));ft.setClearColor(0,0);ft.outputColorSpace=pt;document.body.appendChild(ft.domElement);ft.toneMapping=eo;ft.toneMappingExposure=1;var cn=new Xs,I0=new Wl,L0=new ta(ft),mv=L0.fromScene(I0,.04);L0.dispose();cn.environment=mv.texture;I0.dispose();cn.add(new $s(15266559,2437955,1.6));var wd=new yi(16777215,3.2);wd.position.set(-3,6,5);cn.add(wd);var Ad=new yi(7588077,1.4);Ad.position.set(4,2,-2);cn.add(Ad);var yt=new Yt(34,1,.1,100),St=new Bl(yt,ft.domElement);ft.domElement.tabIndex=0;ft.domElement.setAttribute("aria-label","3D \u7535\u8111\u6A21\u578B\uFF0C\u62D6\u52A8\u6216\u65B9\u5411\u952E\u65CB\u8F6C");ft.domElement.addEventListener("keydown",i=>{if(i.key==="Escape"&&Qn.active){i.preventDefault(),window.chrome?.webview?.postMessage({type:"dismissFocus",requestId:Qn.requestId});return}let e={ArrowLeft:[-12,0],ArrowRight:[12,0],ArrowUp:[0,-12],ArrowDown:[0,12]};e[i.key]&&(i.preventDefault(),Ld({type:"drag",dx:e[i.key][0],dy:e[i.key][1]}))});St.enablePan=!1;St.enableDamping=!0;St.dampingFactor=.12;St.enableZoom=!1;St.minPolarAngle=.15;St.maxPolarAngle=Math.PI*.75;St.addEventListener("change",$t);St.addEventListener("start",()=>{Ei=null,Bn=!1,window.chrome?.webview?.postMessage({type:"autoStopped"})});var Us=0,ca=!1,$l=0,hi=!1,bn,Bn=!1,E0=0,tt={},Yn=!1,yd=0,Sd="",Ei=null,Rd=null,wo=null,cr=!1,gt=null,ui=!1,Jn=0,os=null,Xl=null,So=0,Ed=!1,D0=0,aa=0,Kl=0,Td=0,jl=yt.clone(),vo=new Map,Yl=1,Ns=null,N0=1100,T0=0,oa=[],Ql=[],Eo=[],or=4,F0=new $i({color:9746901,transparent:!0,opacity:.82}),gv=new $i({color:6806015,transparent:!0,opacity:.95}),la=document.createElementNS("http://www.w3.org/2000/svg","svg");la.setAttribute("viewBox","0 0 1000 1000");la.setAttribute("preserveAspectRatio","none");la.style.cssText="position:absolute;inset:0;width:100%;height:100%;pointer-events:none";document.body.append(la);function $t(){!Us&&!ca&&(Us=requestAnimationFrame(xv))}function Cd(){let i={};for(let[e,t]of Object.entries(Fs)){let n=t.getWorldPosition(new P).project(yt);i[e]=[(n.x+1)/2,(1-n.y)/2]}return i}function bv(i){if(tt.embedded||!tt.connectors){la.innerHTML="";return}la.innerHTML=Object.entries(i).filter(([e])=>tt.labels?.includes(e)).map(([e,[t,n]])=>{let s=tt.starts?.[e];return s?`<line x1="${s[0]*1e3}" y1="${s[1]*1e3}" x2="${t*1e3}" y2="${n*1e3}" stroke="#69c6e4" stroke-width="2.3" opacity=".7"/>`:""}).join("")}function xv(i){if(Us=0,ca||!Lt||document.hidden||Ed)return;if(tt.inspection&&(cs.active||oa.length)&&!cr&&!Ei&&!hi&&!os&&!ui&&i-T0<(Yn?250:1e3/24)){$t();return}T0=i;let e=Math.min(.05,yd?(i-yd)/1e3:1/60);yd=i;let t=tt.inspection?cs.active?28:4:8;if(or+=(t-or)*(1-Math.exp(-e*2.4)),!cr&&(!tt.inspection||!Yn)){for(let l of oa)l.rotation.z-=e*or;if(!Yn)for(let{rotor:l,rate:h}of Ql)l.rotation.z-=e*or*h}for(let l of Eo)l.opacity=!tt.inspection||Yn?0:Math.max(0,(or-4)/24)*.16;if(hi){let l=Yn?1:Math.min(1,(i-$l)/1500),h=1-Math.pow(1-l,3);bn&&(bn.rotation.x=Math.PI/2-(Math.PI/2+.19)*h),Lt.rotation.y=.45*(1-h),di.opacity=tt.laptop===!1?Math.max(0,(l-.35)/.65):Math.max(0,(l-.45)/.55),gv.opacity=tt.laptop===!1?h:.95,l===1&&(hi=!1,setTimeout(()=>{Pd(!1),window.chrome?.webview?.postMessage({type:"booted"})},0))}if(Ei){let l=Ei,h=Yn?1:Math.min(1,(i-l.start)/850),u=h*h*h*(10+h*(-15+6*h));yt.position.lerpVectors(l.from,l.to,u),St.target.lerpVectors(l.fromTarget,l.target,u),yt.lookAt(St.target),h===1&&(Ei=null)}let n=Qn.update(i,e);St.autoRotate=Bn&&!hi&&!Qn.active,St.autoRotateSpeed=.5,Qn.active||St.update();let s=!1;if(gt&&(ui||Jn>0)&&(s=gt.update(i)),os){let l=os,h=Yn?1:Math.min(1,(i-l.start)/N0),u=h*h*h*(10+h*(-15+6*h));Jn=Ht.lerp(l.from,l.to,u),h===1&&(os=null,ui||(gt.scene.visible=!1))}if(Ns){let l=Ns,h=Yn?1:Math.max(0,Math.min(1,(i-l.start)/l.duration)),u=h*h*h*(10+h*(-15+6*h));Jl(Ht.lerp(l.from,l.to,u)),h===1&&(Ns=null)}cn.children.filter(l=>l.isLight).forEach(l=>{Jn>0?(l.userData.inspectionIntensity??=l.intensity,l.intensity=l.userData.inspectionIntensity*(1-Jn)):l.userData.inspectionIntensity!==void 0&&(l.intensity=l.userData.inspectionIntensity,delete l.userData.inspectionIntensity)});let r=yt;gt&&Jn>0&&(gt.setLightWeight(Jn),jl.copy(yt),jl.position.lerpVectors(yt.position,gt.camera.position,Jn),jl.lookAt(St.target.clone().lerp(gt.targetPoint,Jn)),r=jl),cn.updateMatrixWorld(!0),yt.updateMatrixWorld(!0);let a=ui?{}:Cd();cs.update(i,a,Yn),ft.render(cn,r),bv(a);let o=JSON.stringify(a),c=lr.update(i);!tt.inspection&&i-E0>32&&o!==Sd&&(E0=i,Sd=o,window.chrome?.webview?.postMessage({type:"anchors",anchors:a})),(hi||Bn||!cr&&!ui&&oa.length&&(!tt.inspection||!Yn)||n||Ei||cs.active||os||Ns||s||c)&&$t()}function Jl(i){if(!(!Lt||i===Yl)){Yl=i,Lt.visible=i>0,i<1&&!vo.size&&Lt.traverse(e=>{for(let t of e.material?Array.isArray(e.material)?e.material:[e.material]:[])vo.has(t)||vo.set(t,{opacity:t.opacity,transparent:t.transparent})});for(let[e,t]of vo){let n=i<1||t.transparent;e.transparent!==n&&(e.transparent=n,e.needsUpdate=!0),e.opacity=t.opacity*i}i===1&&vo.clear()}}function Pd(i=!1){if(!Lt)return;ft.render(cn,yt);let e={type:"rendered",anchors:Cd()};return i&&(e.image=ft.domElement.toDataURL("image/png")),window.chrome?.webview?.postMessage(e),window.lastResult=e,e}function To(){let i=innerWidth||720,e=innerHeight||690,t=performance.now()<D0,n=t?Math.max(aa,i):i,s=t?Math.max(Kl,e):e;(n!==aa||s!==Kl)&&(ft.setSize(n,s,!1),aa=n,Kl=s),ft.domElement.style.width=i+"px",ft.domElement.style.height=e+"px",yt.aspect=i/e,yt.fov=tt.inspection?34:Ht.radToDeg(2*Math.atan(Math.tan(34*Math.PI/360)*e/360)),yt.updateProjectionMatrix(),gt?.resize(i,e),tt.inspection&&Lt&&!Qn.active&&!wo&&(cr?H0():z0()),$t()}addEventListener("resize",To);To();var Zn=new mt({color:8161685,metalness:.65,roughness:.38,envMapIntensity:.65}),Rt=new mt({color:2107187,metalness:.08,roughness:.7}),$n=new mt({color:1053981,metalness:.05,roughness:.8}),on=new mt({color:527895,metalness:.06,roughness:.7}),Mo=new mt({color:6870783,emissive:40398,emissiveIntensity:1.2,metalness:.25,roughness:.25}),Mt=new mt({color:4346209,metalness:.45,roughness:.4}),_v=new mt({color:7438737,metalness:.42,roughness:.58}),U0=new mt({color:2309472,metalness:.5,roughness:.13}),Zl=new mt({color:2646147,roughness:.65}),w0=new mt({color:10914385,metalness:.52,roughness:.43}),A0=new mt({color:7835811,metalness:.35,roughness:.48}),Lt,Fs={},di,Qn=new Gl({scene:cn,camera:yt,controls:St,canvas:ft.domElement,getModel:()=>Lt,getMarker:i=>Fs[i]||Fs.\u4E3B\u677F,getScreenTexture:()=>di?.map,reduced:()=>Yn,requestFrame:$t,prepareObject:yv,post:i=>window.chrome?.webview?.postMessage(i),restoreAuto:i=>{Bn=i}}),cs=new Vl(()=>di,wd,Ad),lr=new ql;async function yv(i){let e=new at,t=[],n=new Set;e.scale.setScalar(1e-5),e.position.copy(Lt.position),e.add(i),cn.add(e),i.traverse(s=>{if(s.isMesh){t.push([s,s.frustumCulled]),s.frustumCulled=!1;for(let r of Array.isArray(s.material)?s.material:[s.material])for(let a of Object.values(r))a?.isTexture&&n.add(a)}});try{for(let s of n)ft.initTexture(s);await ft.compileAsync(i,yt,cn),ft.render(cn,yt)}finally{cn.remove(e),e.remove(i);for(let[s,r]of t)s.frustumCulled=r;i.updateMatrixWorld(!0)}}function vv(i,e,t,n,s,r,a){let o=new ke(new Vn(e,t),a);return o.position.set(n,s,r),i.add(o),o}function vt(i,e,t,n,s){let r=new At;r.position.set(t,n,s),e.add(r),Fs[i]=r}function Mv(i,e,t,n,s,r,a){let o=new Di($(e,t,n),10),c=o.attributes.position,l=o.attributes.uv;for(let u=0;u<l.count;u++)l.setXY(u,c.getX(u)/e+.5,c.getY(u)/t+.5);let h=new ke(o,di);h.position.set(s,r,a),i.add(h)}function vd(i,e,t,n,s){let r=Nt(i,[[s*.6,.001],[s*.64,.005],[s*.89,.005],[s,.002],[s,0],[s*.6,0],[s*.6,.001]],e,t,n,Mt,32);r.rotation.x=Math.PI/2;let a=new ke(new vs(s*.62,24,12),U0);a.scale.z=.31,a.position.set(e,t,n+.001),i.add(a)}function O0(i,e,t,n,s,r,a=!1){let o=e-(a?.108:.062),c=o/(a?1.6:16/9),l=s+(a?-.008:.009),h=r+(a?.02:.03),u=$(e,t,a?.077:.048);Re(i,u,a?.036:.049,n,s,r,Zn,a?.004:.0045,12),a||Nl(i,e,n,s,r,{shell:Rt,dark:on,metal:Zn,trim:Mt,portBlue:Zl}),Re(i,$(e-(a?.02:.03),t-.019,a?.06:.035),.008,n,s,h,Rt,.002,10);let d=$(e-(a?.048:.033),t-(a?.036:.024),a?.049:.026);if(Re(i,d,.004,n,s,h+.006,on,.001,12),Mv(i,o,c,a?.035:.012,n,l,h+.012),a){let m=s+t/2-.027;Re(i,$(.196,.035,.018),.002,n,m,h+.014,$n,.001,9),vd(i,n,m,h+.017,.013),vd(i,n+.047,m,h+.017,.0085),Re(i,$(.038,.007,.0035),.002,n-.028,s+t/2-.009,h+.016,Mt,5e-4,6);for(let b=0;b<3;b++)he(i,.002,.004,9e-4,n-.035+b*.006,s+t/2-.009,h+.0175,on);for(let b of[-.119,.107])We(i,.0031,.001,n+b,m,h+.0132,Mt,14).rotation.x=Math.PI/2;return m}let f=s+t/2+.054;Re(i,$(.196,.066,.027),.079,n,f,r+.028,Rt,.0045,12),Re(i,$(.172,.049,.022),.006,n,f,r+.071,on,.002,10),vd(i,n,f,r+.077,.021);for(let m of[-.063,.063])for(let b=0;b<3;b++)We(i,.002,.001,n+m,f-.006+b*.005,r+.076,Mt,9).rotation.x=Math.PI/2;return he(i,.089,.025,.092,n,s+t/2+.001,r-.011,$n,.007),he(i,.073,.062,.018,n,s+t/2-.024,r-.059,Rt,.006).rotation.x=.24,We(i,.014,.094,n,s+t/2+.014,r-.03,Mt,24).rotation.z=Math.PI/2,he(i,.019,.004,.003,n+e*.44,s-t/2+.012,h+.012,Mo,.001),f}var Sv=[..."1234567890"].map(i=>[i,1]),Md=i=>[...i].map(e=>[e,1]),R0=[[["esc",1],...Array.from({length:12},(i,e)=>["F"+(e+1),1]),["del",1.5]],[["~",1],...Sv,["-",1],["=",1],["\u232B",1.5]],[["tab",1.35],...Md("QWERTYUIOP[]"),["\\",1.15]],[["caps",1.55],...Md("ASDFGHJKL;'"),["enter",1.95]],[["shift",1.95],...Md("ZXCVBNM,./"),["shift",2.55]],[["ctrl",1.3],["fn",1],["\u229E",1],["alt",1.1],["",5.2],["alt",1.1],["\u2190",.9],[`\u2191
\u2193`,1],["\u2192",1]]];function B0(i,e,t,n,s,r,a,o=!1){let c=document.createElement("canvas");c.width=1024,c.height=512;let l=c.getContext("2d");l.textAlign="center",l.textBaseline="middle",l.fillStyle="#dce8f2";let h=new xn(c);h.colorSpace=pt,h.anisotropy=Math.min(4,ft.capabilities.getMaxAnisotropy());let u=new jt({map:h,transparent:!0,toneMapped:!1,depthWrite:!1});u.userData.ownsMap=!0;let d=o?n:n*.846,f=e-(n-d)/2,m=o?[.76,1,1,1,1,1]:[.88,1,1,1,1,1],b=s/m.reduce((M,E)=>M+E,0),g=t-s/2,p=o?R0:[...R0.slice(0,-1),[["ctrl",1.25],["\u229E",1.25],["alt",1.25],["",6.25],["alt",1.25],["fn",1.25],["ctrl",1.25]]],_=(M,E,A,y,w,R,I)=>{let L=A%16,F=Math.floor(A/16);l.font=(M.length>2?"19":"25")+"px Segoe UI, sans-serif",l.fillText(M,L*64+32,F*36+(E?25:18),57),E&&(l.font="11px Segoe UI, sans-serif",l.fillText(E,L*64+32,F*36+8,51));let D=vv(i,R,I,y,r+a+(o?5e-4:.003),w,u);D.rotation.x=-Math.PI/2;let k=D.geometry.attributes.uv;for(let W=0;W<k.count;W++)k.setXY(W,(L*64+k.getX(W)*64)/1024,1-(F*36+(1-k.getY(W))*36)/512)},S=new mt({color:4740966,roughness:.64}),x=(M,E,A,y,w)=>{let R=o?1:1+.028*(2-w),I=_e(i,M,E,o?.017:.016,a-.005,A,r+a/2-.001,y,$n,.0025,4),L=I.geometry.attributes.position;for(let F=0;F<L.count;F++){let D=Ht.clamp(L.getZ(F)/a+.5,0,1),k=(o?.004:.015)*D,W=(o?.005:.014)*D,X=L.getY(F),re=o?0:(w-2)*.016;L.setXYZ(F,L.getX(F)*(1-k/M),X*(1-W/E),L.getZ(F)*R+X*re)}if(I.geometry.computeVertexNormals(),!o&&(he(i,Math.min(.058,M*.64),.016,.052,A,r-.007,y,S,.002),M>.18))for(let F of[-.29,.29])he(i,.033,.011,.046,A+F*M,r-.008,y,Mt,.002)};for(let[M,E]of p.entries()){let A=m[M]*b,y=d/E.reduce((R,[,I])=>R+I,0),w=f-d/2;for(let[R,[I,L]]of E.entries()){let F=y*L,D=w+F/2,k=g+A/2,W=I.includes(`
`),X=I.split(`
`);w+=F;for(let[re,j]of X.entries()){let ee=(A-.019)/X.length,te=k+(W?(re-.5)*ee:0),Ne=F-.022,Se=ee-(W?.009:.002);if(x(Ne,Se,D,te,M),!j)continue;let ct=M*32+R*2+re,$e=["~","!","@","#","$","%","^","&","*","(",")","_","+"][R],ht=M===1&&R>0&&R<13?$e:null;_(!o&&M===0&&j==="del"?"ins":j,ht,ct,D,te,Math.min(F-.032,y*.84),ee*.79)}}g+=A}if(!o){let M=e+n*.425,E=t+s*.293,A=[["del",-.055,-.294],["home",.055,-.294],["pgup",-.055,-.2],["pgdn",.055,-.2],["\u2191",0,-.014],["\u2190",-.093,.079],["\u2193",0,.079],["\u2192",.093,.079]];for(let[y,[w,R,I]]of A.entries())x(.083,.079,M+R,E+I,y<4?1:4),_(w,null,192+y,M+R,E+I,.072,.06)}}function Ev(i){let e=new at;e.position.set(-1.03,0,1.04),i.add(e);let t=_e(e,2.224,.7,.073,.037,0,.034,0,Zn,.006,9),n=t.geometry.attributes.position;for(let o=0;o<n.count;o++){let c=Ht.clamp((n.getY(o)+.35)/.7,0,1),l=Ht.clamp(n.getZ(o)/.037+.5,0,1);n.setZ(o,n.getZ(o)+c*l*.063)}t.geometry.computeVertexNormals();let s=new at;s.position.y=.086,s.rotation.x=.091,e.add(s);let r=$(2.203,.686,.063);r.holes.push($(2.115,.612,.04,0,0,new Qe)),Re(s,r,.014,0,0,0,Mt,.003,7).rotation.x=-Math.PI/2,_e(s,2.112,.611,.04,.007,0,-.003,0,Rt,.001,8),B0(s,0,0,2.1,.603,.015,.041);for(let o of[-1.052,1.052])for(let c of[-.292,.292])We(s,.0095,.003,o,.0075,c,Zn,16),he(s,.013,.0012,.002,o,.0094,c,on).rotation.y=.72;Nt(s,[[0,.004],[.032,.004],[.038,.009],[.038,.028],[.031,.032],[0,.032]],.947,.008,-.244,Mt,36);for(let o=0;o<24;o++){let c=o*Math.PI/12;he(s,.003,.015,.003,.947+Math.cos(c)*.038,.026,-.244+Math.sin(c)*.038,Rt).rotation.y=-c}let a=$(.078,.03,.012);a.holes.push($(.055,.019,.008,0,0,new Qe)),Re(e,a,.006,-.794,.089,-.35,Mt,0,6).rotation.y=Math.PI,he(e,.048,.011,.006,-.794,.089,-.347,on,.002),he(e,.035,.004,.006,-.794,.089,-.354,Rt,.001);for(let o of[-.887,.887])for(let c of[-.244,.232])_e(e,.212,.085,.039,.012,o,.012,c,$n,.002,7);return gn(s),gn(e),e}function Tv(i,e,t,n,s,r){let a=e*1.65,o=.09,c=$(n+.024,s+.015,r==="c"?.021:.009),l=$(n,s,r==="c"?s/2:.006,0,0,new Qe);c.holes.push(l);let h=Re(i,c,.009,a,o,t,Mt,.0014,10);h.rotation.y=e*Math.PI/2;let u=Re(i,$(n-.003,s-.002,r==="c"?.015:.005),.004,a-e*.01,o,t,on,0,10);if(u.rotation.y=e*Math.PI/2,r==="card"){he(i,.008,.005,n*.73,a-e*.005,o-.006,t,Rt,.001);return}let d=r==="c"?.007:.011,f=o+(r==="c"?0:-.007);he(i,.013,d,n*.75,a-e*.001,f,t,r==="a"?Zl:$n,.0015);let m=r==="c"?12:r==="video"?9:4,b=n*.66/m;for(let g=0;g<m;g++){let p=t+(g-(m-1)/2)*b;he(i,.01,.0016,b*.48,a-e*.003,f+d/2+.001,p,w0),(r==="c"||r==="video")&&he(i,.01,.0016,b*.48,a-e*.003,f-d/2-.001,p,w0)}}function wv(){let i=new at,e=$(3.306,2.182,.112);e.holes.push($(2.93,1.013,.042,0,.332,new Qe)),e.holes.push($(1.491,.647,.062,0,-.548,new Qe));for(let l of[-1,1])for(let h=0;h<3;h++)for(let u=0;u<34;u++)Vt(e,.0053,l*(1.514+h*.022),.826-u*.027);let t=Re(i,e,.014,0,.127,0,Zn,.0028,7);t.rotation.x=-Math.PI/2;let n=_e(i,3.264,2.144,.103,.074,0,.079,0,Mt,.006,11),s=$(3.258,2.144,.107);for(let l of[-1,1])for(let h=0;h<20;h++)for(let u of[.537,.742]){let d=l*(.333+h*.052);s.holes.push($(.025,.125,.012,d,u,new Qe))}let r=Re(i,s,.023,0,.03,0,Zn,0,5);r.rotation.x=-Math.PI/2;for(let[l,h]of[[n,.5],[r,1]]){let u=l.geometry.attributes.position;for(let d=0;d<u.count;d++){let f=Ht.clamp((1.08-u.getY(d))/2.16,0,1),m=l===n?Math.max(0,.5-u.getZ(d)/.074):1;u.setZ(d,u.getZ(d)+.03*f*h*m)}l.geometry.computeVertexNormals()}_e(i,2.919,1.004,.039,.006,0,.119,-.332,Rt,.001,9),B0(i,0,-.333,2.865,.946,.123,.02,!0),_e(i,1.488,.645,.06,.004,0,.123,.548,on,.001,10),_e(i,1.469,.624,.056,.01,0,.128,.548,_v,.0018,12);for(let l of[-1,1]){_e(i,.087,.934,.034,.002,l*1.536,.123,-.38,on,5e-4,7);for(let h=0;h<34;h++)he(i,.066,.0014,.003,l*1.536,.125,-.826+h*.027,$n);for(let[h,u]of[[-.906,.008],[.78,.027]]){_e(i,.46,.066,.033,.018,l*1.025,u,h,$n,.003,8),_e(i,.43,.048,.023,.003,l*1.025,u-.01,h,Rt,.001,6);for(let d=0;d<12;d++)he(i,.003,.0015,.03,l*1.025+(d-5.5)*.031,u-.012,h,Mt).rotation.y=.25}for(let h of[-.92,-.02,.87]){let u=.017+.03*(1.08+h)/2.16;We(i,.026,.003,l*1.46,u,h,Mt,20),We(i,.018,.003,l*1.46,u-.0025,h,Zn,18);for(let d of[.72,.72+Math.PI/2])he(i,.019,.0012,.0031,l*1.46,u-.0042,h,on).rotation.y=d}_e(i,1.079,.356,.04,.002,l*.823,.045,-.638,on,0,5);for(let h=0;h<20;h++){let u=l*(.333+h*.052);he(i,.02,.003,.292,u,.043,-.641,$n);for(let d of[-.711,-.563])he(i,.02,.003,.003,u,.041,d,Mt)}for(let h of[.55,1.35]){let u=We(i,.016,.003,l*h,.013,-.965,Mt,16);u.rotation.z=.006;for(let d of[0,Math.PI/3,Math.PI*2/3])he(i,.015,.001,.003,l*h,.011,-.965,on).rotation.y=d}he(i,.002,.003,1.778,l*1.634,.061,-.004,Rt)}for(let[l,h,u,d,f]of[[-1,-.67,.094,.038,"c"],[-1,-.475,.094,.038,"c"],[-1,-.222,.172,.052,"video"],[1,-.591,.149,.047,"a"],[1,-.378,.094,.038,"c"],[1,.081,.231,.024,"card"]])Tv(i,l,h,u,d,f);let a=Nt(i,[[.016,-.007],[.025,-.007],[.029,-.003],[.028,.004],[.017,.004],[.016,-.007]],1.65,.087,-.16,Mt,32);a.rotation.z=-Math.PI/2,We(i,.016,.004,1.639,.087,-.16,on,24).rotation.z=Math.PI/2;for(let l of[-1.01,1.01])We(i,.035,.45,l,.137,-1.038,Mt,36).rotation.z=Math.PI/2;We(i,.03,1.58,0,.137,-1.039,Rt,36).rotation.z=Math.PI/2;let o=$(2.51,.043,.014);for(let l=0;l<42;l++)o.holes.push($(.034,.019,.007,(l-20.5)*.055,0,new Qe));Re(i,o,.01,0,.086,-1.094,Rt,0,4).rotation.y=Math.PI,he(i,2.428,.022,.006,0,.086,-1.079,on,.002);for(let l=0;l<42;l++)he(i,.006,.017,.014,(l-20.5)*.055,.086,-1.084,Mt).rotation.y=.27;for(let l of[-1.23,-.78,.78,1.23])We(i,.035,.022,l,.137,-1.038,Zn,24).rotation.z=Math.PI/2,We(i,.031,.024,l,.137,-1.038,Rt,22).rotation.z=Math.PI/2;he(i,.44,.012,.013,0,.105,1.085,Mt,.004),bn=new at,bn.position.set(0,.143,-1.039),bn.rotation.x=-.19,i.add(bn);let c=O0(bn,3.31,2.06,0,1.03,0,!0);return vt("\u663E\u793A\u5668\u4E2D\u5FC3",bn,0,1.03,.04),vt("\u626C\u58F0\u5668",i,1.53,.137,-.4),vt("\u6444\u50CF\u5934",bn,0,c,.03),vt("\u952E\u76D8",i,0,.145,-.35),vt("\u6563\u70ED",i,0,.138,-.8),vt("\u663E\u793A\u5668",bn,-.65,1.22,.04),vt("\u5904\u7406\u5668",i,-.72,.149,-.44),vt("\u5185\u5B58",i,.82,.149,-.44),vt("\u663E\u5361",i,.48,.149,-.06),vt("\u786C\u76D8",i,-1.1,.138,.5),vt("\u7535\u6C60",i,.92,.138,.66),vt("\u4E3B\u677F",i,0,.149,-.1),vt("BIOS",i,.1,.149,-.4),vt("\u7CFB\u7EDF",bn,.7,.5,.04),vt("NPU",i,-.4,.149,-.5),vt("\u58F0\u5361",i,1.65,.087,-.16),vt("\u7F51\u5361",i,1.6,.087,-.35),gn(i),gn(bn),yt.position.set(4.3,3.15,6.5),St.target.set(0,.96,0),yt.lookAt(St.target),i}function Av(){let i=new at,e=new at;e.position.set(-1.32,0,-.08),i.add(e),_e(e,.94,.63,.098,.031,0,.035,.015,Rt,.009,11),_e(e,.81,.49,.078,.014,0,.02,.015,$n,.005,10);let t=$(.149,1.355,.041);t.holes.push($(.067,.192,.031,0,-.294,new Qe)),Re(e,t,.133,0,.747,-.219,Zn,.006,7),he(e,.14,.373,.025,0,1.092,-.293,Mt,.009),he(e,.325,.243,.095,0,1.403,-.228,Rt,.041),We(e,.047,.237,0,1.411,-.258,Mt,24).rotation.z=Math.PI/2;let n=O0(e,2.64,1.512,0,1.62,-.08);vt("\u663E\u793A\u5668",e,-.45,1.67,.02),vt("\u663E\u793A\u5668\u4E2D\u5FC3",e,0,1.62,-.026),vt("\u6444\u50CF\u5934",e,0,n,.021),vt("\u952E\u76D8",i,-1.03,.1,1.04),gn(e);let s=new at;s.position.set(1.38,0,-.17),i.add(s);let r=$(1.034,2.288,.044);Vt(r,.306,-.13,1.837-1.236),r.holes.push($(.155,.77,.017,.357,1.698-1.236,new Qe)),r.holes.push($(.455,.177,.011,-.132,1.11-1.236,new Qe)),r.holes.push($(.559,.251,.019,-.037,.253-1.236,new Qe));for(let u=0;u<6;u++)r.holes.push($(.451,.039,.006,-.132,.663+u*.06-1.236,new Qe));Re(s,r,.048,0,1.236,-.765,Rt,0,7);let a=$(1.034,1.57,.066);for(let u=0;u<16;u++)for(let d of[-1,1])a.holes.push($(.019,.295,.0095,-.352+u*.047,.175+d*.16,new Qe));let o=Re(s,a,.033,0,2.406,0,Rt,.004,8);o.rotation.x=-Math.PI/2,he(s,1.05,.075,1.58,0,.079,0,Rt,.029),he(s,.055,2.31,1.54,.492,1.235,0,Rt,.023);for(let u of[-.74,.741])he(s,.06,2.26,.092,-.499,1.234,u,Rt,.026);for(let u of[.148,2.324])he(s,.055,.084,1.48,-.496,u,0,Rt,.026);he(s,.019,2.096,1.38,-.532,1.234,0,new _n({color:8105162,roughness:.24,transparent:!0,opacity:.11,depthWrite:!1}),.006);for(let u of[.216,2.25])for(let d of[-.64,.64]){We(s,.029,.006,-.543,u,d,$n,24).rotation.z=Math.PI/2,We(s,.019,.011,-.55,u,d,Mt,20).rotation.z=Math.PI/2,We(s,.012,.005,-.557,u,d,Zn,18).rotation.z=Math.PI/2;for(let f of[0,Math.PI/3,Math.PI*2/3])he(s,.001,.015,.003,-.56,u,d,on).rotation.x=f}t0(s,{edge:Rt,trim:Mt,dark:on,aluminum:Zn,portBlue:Zl,accent:Mo,fanBlade:A0},Ql,Eo);for(let u of[-.352,.352])for(let d of[-.552,.552]){_e(s,.174,.196,.042,.016,u,.041,d,Mt,.004,9),_e(s,.151,.174,.032,.07,u,-.001,d,$n,.008,8),_e(s,.137,.157,.025,.006,u,-.039,d,on,.001,7);for(let f=0;f<7;f++)he(s,.105,.003,.006,u,-.042,d+(f-3)*.019,Rt)}for(let u=0;u<22;u++)he(s,.68,.005,.005,0,2.382,-.486+u*.028,Mt,.001);for(let[u,d,f,m]of[[.025,.08,.041,!1],[.135,.08,.041,!1],[.232,.058,.03,!0]]){let b=$(d,f,m?.014:.006);b.holes.push($(d-.013,f-.01,m?.01:.004,0,0,new Qe)),Re(s,b,.006,u,2.432,.52,Mt,0,5).rotation.x=-Math.PI/2,he(s,d-.016,.003,f-.012,u,2.427,.52,on),he(s,d*.65,.003,f*.28,u,2.43,.52,m?Rt:Zl)}We(s,.055,.011,.37,2.433,.52,Mt,30),We(s,.043,.012,.37,2.437,.52,Rt,28),We(s,.015,.002,.37,2.444,.52,Mo,20),Nt(s,[[.01,0],[.018,0],[.018,.006],[.011,.006],[.01,0]],-.082,2.428,.52,Mt,20);let c=$(1.02,2.268,.052);for(let u=0;u<3;u++)Vt(c,.317,0,.604+u*.65-1.237);Re(s,c,.044,0,1.237,.806,Rt,.006,16);let l=new _n({color:7181482,roughness:.26,transparent:!0,opacity:.055,depthWrite:!1}),h=rr(.274,.022,7);for(let u=0;u<3;u++){let d=.604+u*.65,f=.858,m=$(.641,.609,.037);Vt(m,.28);for(let S of[-.276,.276])for(let x of[-.26,.26])Vt(m,.014,S,x);Re(s,m,.053,0,d,f-.035,Mt,.002,18);let b=new ke(new Ks(.28,72),l);b.position.set(0,d,f-.092),s.add(b);for(let S=0;S<4;S++){let x=S*Math.PI/2+Math.PI*.25,M=he(s,.245,.017,.016,Math.cos(x)*.161,d+Math.sin(x)*.161,f-.069,Rt,.004);M.rotation.z=x}We(s,.06,.026,0,d,f-.064,$n,28).rotation.x=Math.PI/2;let g=new ke(new Ms(.291,.011,10,64),Mo);g.position.set(0,d,f+.022),s.add(g);for(let S of[-.276,.276])for(let x of[-.26,.26]){We(s,.013,.013,S,d+x,f-.003,Zn,18).rotation.x=Math.PI/2;let M=he(s,.013,.003,.002,S,d+x,f+.0045,on);M.rotation.z=S*3+x}let p=new at;p.position.set(0,d,f+.024),s.add(p),oa.push(p),p.add(new ke(h.clone(),A0)),We(p,.072,.035,0,0,.004,Rt,30).rotation.x=Math.PI/2,We(p,.044,.007,0,0,.024,Mt,28).rotation.x=Math.PI/2;let _=new jt({color:14214381,transparent:!0,opacity:0,depthWrite:!1});Eo.push(_);for(let S of[.12,.24]){let x=new ke(h.clone(),_);x.rotation.z=S,x.position.z=.003,p.add(x)}gn(p)}return h.dispose(),Ev(i),s0(i,.46,1.1,{edge:Rt,keys:$n,dark:on,aluminum:Zn,trim:Mt,lens:U0,accent:Mo}),gn(i),gn(s),vt("\u5904\u7406\u5668",s,.066,1.731,-.19),vt("\u5185\u5B58",s,.201,1.746,.278),vt("\u663E\u5361",s,-.389,1.152,.15),vt("\u786C\u76D8",s,.315,1.331,.154),vt("\u4E3B\u677F",s,.37,1.31,-.2),vt("BIOS",s,.352,.806,.388),vt("\u7CFB\u7EDF",e,.7,1.15,.02),vt("NPU",s,.2,1.7,.05),vt("\u58F0\u5361",s,.25,2.44,.52),vt("\u7F51\u5361",s,-.5,1.8,-.7),vt("\u6563\u70ED",s,0,1.254,.883),yt.position.set(-4.7,3.25,6.6),St.target.set(0,1.1,.15),yt.lookAt(St.target),i}async function Id(i={}){if(cr=!1,lr.stop(),Jl(1),Ns=null,++So,os=null,ui=!1,Jn=0,St.enabled=!0,gt&&(gt.setActive(!1),gt.scene.visible=!1),cs.setActive(!1),Qn.close(!0),tt=i,ca=!1,hi=!1,oa.length=0,Ql.length=0,Eo.length=0,or=4,Sd="",To(),ha.style.display=tt.embedded?"none":"flex",Lt){cn.remove(Lt);let t=new Set;Lt.traverse(n=>{n.geometry?.dispose(),n.isMesh&&n.material!==di&&t.add(n.material)});for(let n of t)n.userData.ownsMap&&n.map.dispose(),n.dispose()}di?.map?.dispose(),di?.dispose(),Fs={},bn=null;let e;if(tt.wallpaper)try{e=await new Js().loadAsync(tt.wallpaper),e.colorSpace=pt}catch{tt.wallpaper=null}if(!e){let t=document.createElement("canvas");t.width=512,t.height=320;let n=t.getContext("2d"),s=n.createLinearGradient(0,320,512,0);s.addColorStop(0,"#102349"),s.addColorStop(1,"#5395c9"),n.fillStyle=s,n.fillRect(0,0,512,320),e=new xn(t),e.colorSpace=pt}di=new jt({map:e,toneMapped:!1,transparent:!0,opacity:0}),F0.color.set(tt.dark===!1?3233654:9746901),Lt=tt.laptop===!1?Av():wv(),cn.add(Lt),wo=null,Ei=null,tt.inspection&&z0(),Yn=!!tt.reducedMotion||matchMedia("(prefers-reduced-motion: reduce)").matches,hi=!0,$l=performance.now(),bn&&(bn.rotation.x=Math.PI/2),St.update(),$t()}function k0(){if(!Lt)return;let i=new en().setFromObject(Lt).getBoundingSphere(new mn),e=Math.min(Ht.degToRad(yt.fov),2*Math.atan(Math.tan(Ht.degToRad(yt.fov)/2)*yt.aspect)),t=i.radius/Math.sin(e/2)*.94,n=new P(tt.laptop===!1?-.48:.48,.34,.81).normalize();return{position:i.center.clone().addScaledVector(n,t),target:i.center.clone()}}function z0(){let i=k0();i&&(St.target.copy(i.target),yt.position.copy(i.position),yt.lookAt(St.target),Rd=i)}function H0(){let i=k0();i&&(Rd=i,Ei={start:performance.now(),from:yt.position.clone(),to:i.position,fromTarget:St.target.clone(),target:i.target},$t())}function Rv(i){Ei=null,Bn=!1,hi&&(bn&&(bn.rotation.x=-.19),Lt.rotation.y=0,di.opacity=1,hi=!1),cn.updateMatrixWorld(!0),wo=i.label;let e=Rd;if(!e)return;let t,n;if(!i.label)t=e.target.clone(),n=e.position.clone();else{let s=["\u663E\u793A\u5668","\u7CFB\u7EDF","\u70E4\u673A"].includes(i.label)?"\u663E\u793A\u5668\u4E2D\u5FC3":i.label==="\u58F0\u5361"&&tt.laptop!==!1?"\u626C\u58F0\u5668":i.label;t=(Fs[s]||Fs.\u4E3B\u677F).getWorldPosition(new P),i.label==="\u70E4\u673A"&&t.lerp(Fs.\u6563\u70ED.getWorldPosition(new P),tt.laptop===!1?.5:.28);let a=["\u6444\u50CF\u5934","\u663E\u793A\u5668","\u7CFB\u7EDF","\u70E4\u673A"].includes(i.label),o=i.label==="\u952E\u76D8",c=i.label==="\u70E4\u673A"?tt.laptop===!1?7.4:5.4:i.label==="\u6444\u50CF\u5934"?1.1:o?4:a?4.5:i.label==="\u4E3B\u677F"?3.5:2.8;c*=Math.max(1,.85/yt.aspect);let l=a?new P(.06,.12,1):o||tt.laptop!==!1?new P(.12,1,.68):new P(-1,.32,.45);i.label==="\u6563\u70ED"&&tt.laptop===!1&&l.set(.1,.2,1),n=t.clone().addScaledVector(l.normalize(),c)}Ei={start:performance.now(),from:yt.position.clone(),to:n,fromTarget:St.target.clone(),target:t},$t()}async function C0(){return Xl||(Xl=(async()=>{gt=S0({renderer:ft,invalidate:$t,offset:3.6,scale:.18,floor:!1,post:r=>window.chrome?.webview?.postMessage(r)}),cn.add(gt.scene),gt.scene.visible=!1,gt.setActive(!1),ft.shadowMap.enabled=!0,ft.shadowMap.type=Fc,ft.shadowMap.autoUpdate=!1;let i=new Set;gt.scene.traverse(r=>{r.isMesh&&r.material.map&&i.add(r.material.map)});for(let r of i)ft.initTexture(r);Ed=!0,gt.scene.visible=!0;let e=new Map;try{await ft.compileAsync(cn,yt),Lt.traverse(r=>{for(let a of r.material?Array.isArray(r.material)?r.material:[r.material]:[])e.has(a)||(e.set(a,a.transparent),a.transparent||(a.transparent=!0,a.needsUpdate=!0))}),await ft.compileAsync(cn,yt)}finally{for(let[r,a]of e)r.transparent!==a&&(r.transparent=a,r.needsUpdate=!0);Ed=!1,gt.scene.visible=!1}let t=new wn(8,8),n=ft.getRenderTarget(),s=Lt.visible;Lt.visible=!1,gt.scene.visible=!0;try{ft.setRenderTarget(t),ft.render(cn,gt.camera)}finally{ft.setRenderTarget(n),t.dispose(),Lt.visible=s,gt.scene.visible=!1,gt.setLightWeight(0)}$t()})(),Xl)}function P0(i,e){ui=i,St.enabled=!i,gt.setActive(i),gt.scene.visible=!0,os={from:Jn,to:i?1:0,start:performance.now()},Ns={from:Yl,to:i?0:1,start:performance.now()+(i?150:0),duration:i?1250:850},G0(e,N0+250),i&&(gt.receive({type:"theme",dark:tt.dark}),window.chrome?.webview?.postMessage({type:"keyboardReady"})),$t()}function G0(i,e){D0=performance.now()+e,aa=Math.max(aa,Math.ceil(Number(i)||innerWidth)),ft.setSize(aa,Kl,!1),To(),clearTimeout(Td),Td=setTimeout(To,e+50)}function Ld(i){if(i.type==="inspectionComplete"){if(!tt.inspection||!Lt)return;++So,ui=!1,os=null,Jn=0,Ns=null,Jl(1),Lt.visible=!0,St.enabled=!0,gt&&(gt.setActive(!1),gt.scene.visible=!1),cs.setActive(!1),cr=!!i.active,wo=null,Bn=!1,cr?(lr.start({dark:tt.dark,reduced:Yn||matchMedia("(prefers-reduced-motion: reduce)").matches}),H0()):lr.stop(),$t();return}if(i.type==="prepareKeyboard"){tt.inspection&&C0().then(()=>{i.notify&&(G0(i.width,900),window.chrome?.webview?.postMessage({type:"keyboardPrepared"}))}).catch(e=>window.chrome?.webview?.postMessage({type:"keyboardError",message:String(e)}));return}if(i.type==="keyboardMode"){if(!tt.inspection||!Lt)return;let e=++So;i.active?C0().then(()=>{e===So&&P0(!0,i.width)}).catch(t=>window.chrome?.webview?.postMessage({type:"keyboardError",message:String(t)})):gt&&(ui||Jn>0)&&P0(!1,i.width);return}if(i.type==="keyboard"){ui&&gt?.receive(i.message);return}if(i.type==="stress"){tt.inspection&&di&&(ui||(Ns=null,Jl(1),Lt.visible=!0),cs.setActive(!!i.active),$t());return}if(i.type==="stressSample"){tt.inspection&&cs.setSample(i);return}if(i.type==="inspectionFocus"){tt.inspection&&Lt&&Rv(i);return}if(i.type==="prepareFocus"||i.type==="focus"){if(tt.inspection)return;(i.type==="prepareFocus"?Qn.prepare(i,Bn):Qn.start(i,Bn)).catch(t=>window.chrome?.webview?.postMessage({type:"focusFailed",requestId:i.requestId??0,message:t.message})),Bn=!1;return}if(i.type==="unfocus"){Qn.close(!!i.immediate,i.requestId);return}if(i.type==="pause"){ca=i.value,ca?(cancelAnimationFrame(Us),Us=0):$t();return}if(i.type==="drag"){if(Qn.active){Qn.drag(i.dx,i.dy);return}let e=yt.position.clone().sub(St.target),t=new Ts().setFromVector3(e);t.theta-=i.dx*.008,t.phi=Math.max(.18,Math.min(Math.PI*.75,t.phi-i.dy*.008)),yt.position.copy(St.target).add(new P().setFromSpherical(t)),St.update(),$t();return}if(i.type==="auto"){Bn=!!i.value,$t();return}if(i.type==="replay"){hi=!0,$l=performance.now(),$t();return}if(i.type==="layout"){tt.connectors=i.connectors,tt.starts=i.starts||tt.starts,$t();return}if(i.type==="snapshot"){Pd(!0);return}if(i.type==="theme"){tt.dark=i.dark,lr.dark=!!i.dark,gt?.receive(i),F0.color.set(i.dark?9746901:3233654),$t();return}Id(i).catch(e=>window.chrome?.webview?.postMessage({type:"error",message:e.message}))}addEventListener("visibilitychange",()=>{document.hidden?(cancelAnimationFrame(Us),Us=0):$t()});window.hardwareSnapshot=()=>Pd(!0);window.hardwareSceneState=()=>({opening:hi,lidAngle:bn?.rotation.x,anchors:Cd(),camera:yt.position.toArray(),target:St.target.toArray(),screen:{wallpaper:!!tt.wallpaper,width:di?.map?.image?.width,height:di?.map?.image?.height},completion:lr.state(),render:{frame:Us,calls:ft.info.render.calls,pixelRatio:ft.getPixelRatio()},inspection:tt.inspection?{label:wo,moving:!!Ei,modelVisible:Lt?.visible,modelOpacity:Yl,modelPosition:Lt?.position.toArray(),modelScale:Lt?.scale.toArray()}:null,keyboard:gt?{active:ui,sceneVisible:gt.scene.visible,blend:Jn,moving:!!os,...gt.getState()}:null,stress:cs.state(),autoRotate:Bn,paused:ca,fanSpeed:or,fanTrailOpacity:Eo[0]?.opacity??0,fanAngles:oa.map(i=>i.rotation.z),interiorFanAngles:Object.fromEntries(Ql.map(({id:i,rotor:e})=>[i,e.rotation.z])),focus:Qn.state()});window.receiveHardware=Ld;ft.domElement.addEventListener("webglcontextlost",i=>{i.preventDefault(),window.chrome?.webview?.postMessage({type:"error",message:"WebGL context lost"})});addEventListener("pagehide",()=>{++So,clearTimeout(Td),gt?.dispose(),lr.dispose()});window.renderHardware=Id;window.chrome?.webview?.addEventListener("message",i=>Ld(i.data));var ha=document.createElement("div");ha.className="toolbar";ha.innerHTML='<button id="rotate" aria-pressed="false">\u81EA\u52A8\u65CB\u8F6C</button><button id="replay">\u91CD\u64AD\u5F00\u673A</button>';document.body.append(ha);ha.querySelector("#rotate").onclick=i=>{Bn=!Bn,i.target.setAttribute("aria-pressed",String(Bn)),$t()};ha.querySelector("#replay").onclick=()=>{hi=!0,$l=performance.now(),$t()};window.chrome?.webview?.postMessage({type:"ready"});window.chrome?.webview||(document.body.style.background="#111820",Id({laptop:new URLSearchParams(location.search).get("kind")!=="desktop",labels:["\u5904\u7406\u5668","\u5185\u5B58","\u786C\u76D8","\u663E\u793A\u5668","\u663E\u5361","\u7535\u6C60"],connectors:!1}));})();
/*! Bundled license information:

three/build/three.core.js:
three/build/three.module.js:
  (**
   * @license
   * Copyright 2010-2026 Three.js Authors
   * SPDX-License-Identifier: MIT
   *)
*/
