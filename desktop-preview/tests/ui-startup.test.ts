import {expect,it} from 'vitest';
import {canConfirmUi,createStartupGate} from '../src/lib/ui-startup';

const healthy={mounted:true,visible:true,failed:false,width:1200,height:770,documentHidden:false};
it('requires a mounted visible layout and rejects a hidden document or failed bootstrap',()=>{
  expect(canConfirmUi(healthy)).toBe(true);
  for(const patch of [{mounted:false},{visible:false},{failed:true},{width:0},{height:0},{documentHidden:true}])expect(canConfirmUi({...healthy,...patch})).toBe(false);
});
it('requires two healthy frame observations and resets when visibility is lost',()=>{
  const gate=createStartupGate();expect(gate.observe(healthy)).toBe(false);
  expect(gate.observe({...healthy,visible:false})).toBe(false);
  expect(gate.observe(healthy)).toBe(false);expect(gate.observe(healthy)).toBe(true);
  expect(gate.observe(healthy)).toBe(false);
});
