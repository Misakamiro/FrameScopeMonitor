import {searchItems} from '@/lib/unified-search';
export type ReportSearchEntry={id:string;targetName?:string;createdAt?:string;kind?:string};
export function reportCatalogPage<T extends ReportSearchEntry>(catalog:T[],query:{page:number;search:string;kind:string}) {
  const matching=searchItems(catalog.filter(r=>query.kind==='all'||r.kind===query.kind),query.search,
    r=>[r.targetName||'',r.createdAt||'',r.id],(a,b)=>(Date.parse(b.createdAt||'')||0)-(Date.parse(a.createdAt||'')||0)||a.id.localeCompare(b.id));
  const pages=Math.max(1,Math.ceil(matching.length/50)),page=Math.max(1,Math.min(query.page,pages));
  return {reports:matching.slice((page-1)*50,page*50),total:catalog.length,filteredTotal:matching.length,page,pages,pageSize:50};
}
