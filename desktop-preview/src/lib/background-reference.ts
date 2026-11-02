/** One reference per video source for the lifetime of this WebView. Null means capture failed. */
export function createBackgroundReferences() {
  const videos=new Map<string,number|null>();
  return {
    video(source:string,fallback:number,capture?:()=>number):number {
      if(!source)return fallback;
      if(!videos.has(source)&&capture){
        try{
          const value=capture();
          videos.set(source,Number.isFinite(value)&&value>=0&&value<=1?value:null);
        }catch{videos.set(source,null);}
      }
      return videos.get(source)??fallback;
    },
  };
}

export const backgroundReferences=createBackgroundReferences();
