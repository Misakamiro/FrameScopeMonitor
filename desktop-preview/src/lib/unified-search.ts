import {customPinyin,pinyin} from 'pinyin-pro';
import dictionary from './search-dictionary.json';

customPinyin(dictionary.phrases);
export const searchContractVersion=dictionary.version;
export const normalizeSearch=(value:string)=>value.normalize('NFKC').toLowerCase().replace(/ü/g,'v').trim().replace(/\s+/g,' ');
type Field={raw:string;compact:string;roman:string;initials:string;words:string[]};
const cache=new Map<string,Field>();
function field(value:string):Field {
  const raw=normalizeSearch(value);
  const found=cache.get(raw);if(found)return found;
  const runs=raw.match(/[\p{L}\p{N}]+/gu)||[];
  const roman:string[]=[],initials:string[]=[],words=[...runs];
  for(const run of runs){
    const parts=run.match(/[\p{Script=Han}]+|[^\p{Script=Han}]+/gu)||[];
    for(const part of parts){
      if(/\p{Script=Han}/u.test(part)){
        const syllables=pinyin(part,{toneType:'none',type:'array',v:true});
        roman.push(syllables.join(''));initials.push(syllables.map(s=>s[0]).join(''));
        words.push(syllables.join(''));
      }else{roman.push(part);initials.push(part);}
    }
  }
  const result={raw,compact:raw.replace(/\s/g,''),roman:roman.join(''),initials:initials.join(''),words};
  if(cache.size>=4096)cache.clear();cache.set(raw,result);return result;
}
// Optimal-string-alignment Damerau–Levenshtein; transposed adjacent letters cost one.
export function searchDistance(a:string,b:string,limit:number,substring=false):number {
  const x=Array.from(a),y=Array.from(b);
  if(!substring&&Math.abs(x.length-y.length)>limit)return limit+1;
  let previous=Array.from({length:y.length+1},(_,i)=>substring?0:i),before=previous;
  for(let i=1;i<=x.length;i++){
    const current=[i];
    for(let j=1;j<=y.length;j++){
      current[j]=Math.min(current[j-1]+1,previous[j]+1,previous[j-1]+Number(x[i-1]!==y[j-1]));
      if(i>1&&j>1&&x[i-1]===y[j-2]&&x[i-2]===y[j-1])current[j]=Math.min(current[j],before[j-2]+1);
    }
    before=previous;previous=current;
  }
  return substring?previous.reduce((a,b)=>Math.min(a,b),Infinity):previous[y.length];
}
function matchToken(token:string,value:Field):number|null {
  if(value.raw===token||value.compact===token)return 0;
  if(value.raw.startsWith(token)||value.compact.startsWith(token))return 1;
  if(value.raw.includes(token)||value.compact.includes(token))return 2;
  if(value.roman.includes(token)||value.initials.includes(token))return 3;
  const length=Array.from(token).length;
  if(length<4||/\p{N}/u.test(token))return null;
  const limit=Math.min(length<8?1:2,Math.floor(length*.25));
  for(const word of [...value.words,value.roman]){
    if(!/\p{N}/u.test(word)&&searchDistance(token,word,limit,true)<=limit)return 4;
  }
  return null;
}
/** null means no match. Fields are ordered by importance; all query tokens must match. */
export function searchScore(query:string,fields:readonly string[]):number|null {
  const tokens=normalizeSearch(query).split(' ').filter(Boolean);if(!tokens.length)return 0;
  const values=fields.map(value=>field(value||'')),scores:number[]=[];
  for(const token of tokens){
    let best=Infinity;
    values.forEach((value,index)=>{const tier=matchToken(token,value);if(tier!==null)best=Math.min(best,tier*10+Math.min(index,5));});
    if(!Number.isFinite(best))return null;scores.push(best);
  }
  return Math.max(...scores)*100+scores.reduce((a,b)=>a+b,0);
}
export const searchMatches=(query:string,fields:readonly string[]|string)=>searchScore(query,typeof fields==='string'?[fields]:fields)!==null;
export function searchItems<T>(items:readonly T[],query:string,fields:(item:T)=>readonly string[],compare?:(a:T,b:T)=>number):T[] {
  return items.map((item,index)=>({item,index,score:searchScore(query,fields(item))})).filter(row=>row.score!==null)
    .sort((a,b)=>a.score!-b.score!||compare?.(a.item,b.item)||a.index-b.index).map(row=>row.item);
}
