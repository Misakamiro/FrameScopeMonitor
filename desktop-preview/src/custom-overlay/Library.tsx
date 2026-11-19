import {useEffect,useState} from 'react';
import {Box,Button,HStack,Input,SimpleGrid,Text,VStack} from '@chakra-ui/react';
import {useNavigate} from 'react-router-dom';
import {nativeOperation} from '@/preview/runtime/platform';
import {PreviewDialog,useFloatingFieldStyle} from '@/preview/pages/shared';
import {LayoutRenderer} from './Renderer';
import type {LayoutStore,SavedLayout} from './model';

export default function LayoutLibrary({active,onApply}:{active:boolean;onApply:()=>void|Promise<void>}){
  const navigate=useNavigate(),field=useFloatingFieldStyle();
  const [store,setStore]=useState<LayoutStore|null>(null),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  const [editing,setEditing]=useState<{item:SavedLayout;action:'rename'|'copy'|'delete'}|null>(null),[name,setName]=useState('');
  useEffect(()=>{let alive=true;void nativeOperation<LayoutStore>('custom_overlay_get').then(value=>{if(alive)setStore(value);}).catch(cause=>{if(alive)setError(String(cause));});return()=>{alive=false;};},[]);
  async function action(item:SavedLayout,action:string){if(!store)return;setBusy(true);setError('');try{
    const next=await nativeOperation<LayoutStore>('custom_overlay_'+action,{revision:store.revision,id:item.id,layout_revision:item.revision,name});setStore(next);setEditing(null);
    if(action==='apply')await onApply();
  }catch(cause){setError(String(cause));}finally{setBusy(false);}}
  return <VStack align="stretch" spacing={3} minW={240} flex={1}>
    <HStack justify="space-between"><Text fontSize="sm" fontWeight="medium">我的布局</Text><Button size="xs" onClick={()=>navigate('/overlay-panel/custom?id=new')}>新建布局</Button></HStack>
    {error&&<Text fontSize="sm" color="red.400" role="alert">{error}</Text>}
    {store?.layouts.length===0&&<Text fontSize="sm" opacity={.7}>保存后的自定义布局会显示在这里。</Text>}
    <SimpleGrid columns={1} spacing={3} maxH="460px" overflowY="auto">
      {store?.layouts.map(item=><Box key={item.id} border="2px solid" borderColor={active&&store.applied_id===item.id?'var(--framescope-primary)':'var(--framescope-control-border)'} borderRadius="xl" p={3}>
        <Box as="button" aria-label={'应用布局 '+item.name} disabled={busy} onClick={()=>void action(item,'apply')} width="100%" textAlign="left">
          <Box display="flex" justifyContent="center" bg="var(--framescope-control-bg)" overflow="hidden" borderRadius="md"><LayoutRenderer layout={item.layout} width={220} height={220*item.layout.design_height/item.layout.design_width} data={{}} thumbnail/></Box>
          <Text fontSize="sm" fontWeight="medium" mt={2}>{item.name}</Text>
          {active&&store.applied_id===item.id&&<Text fontSize="xs" opacity={.7}>{store.applied_revision===item.revision?'正在应用':'有已保存的新版本，点击应用'}</Text>}
        </Box>
        <HStack mt={2} wrap="wrap"><Button size="xs" onClick={()=>navigate('/overlay-panel/custom?id='+encodeURIComponent(item.id))}>编辑</Button>{(['rename','copy','delete'] as const).map(action=><Button key={action} size="xs" variant="ghost" isDisabled={busy||(action==='delete'&&active&&store.applied_id===item.id)} onClick={()=>{setName(item.name+(action==='copy'?' 副本':''));setEditing({item,action});}}>{action==='rename'?'重命名':action==='copy'?'复制':'删除'}</Button>)}</HStack>
      </Box>)}
    </SimpleGrid>
    <PreviewDialog title={editing?.action==='delete'?'删除布局':editing?.action==='copy'?'复制布局':'重命名布局'} isOpen={!!editing} onClose={()=>setEditing(null)}>
      {editing?.action==='delete'?<Text>删除“{editing.item.name}”？</Text>:<Input aria-label="布局名称" value={name} onChange={e=>setName(e.target.value)} sx={field.sx}/>}
      <Button mt={4} isLoading={busy} isDisabled={editing?.action!=='delete'&&!name.trim()} onClick={()=>editing&&void action(editing.item,editing.action)}>{editing?.action==='delete'?'删除':'保存'}</Button>
    </PreviewDialog>
  </VStack>;
}
