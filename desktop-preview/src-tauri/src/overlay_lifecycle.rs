//! Short state reservations; never hold this mutex while dispatching/waiting for a window.
use std::{sync::{mpsc,Mutex},time::{Duration,Instant}};
pub const START_BUDGET:Duration=Duration::from_secs(15);
#[derive(Clone,Copy,Debug,PartialEq,Eq)]
pub enum Phase { Stopped, Starting, Running }
struct State {
    generation:u64, completed:u64, desired:bool, phase:Phase, owner:Option<u64>,
    deadline:Option<Instant>, layout:bool, ready:Option<mpsc::Sender<Result<(),String>>>,
}
pub struct Lifecycle {state:Mutex<State>}
impl Lifecycle {
    pub const fn new()->Self {Self{state:Mutex::new(State{generation:0,completed:0,desired:false,phase:Phase::Stopped,owner:None,deadline:None,layout:false,ready:None})}}
    fn reserve<F>(&self,select:F)->u64 where F:FnOnce(&State)->bool {
        let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());
        let desired=select(&state);
        state.generation+=1;state.desired=desired;state.ready=None;
        if desired&&state.phase==Phase::Stopped {state.phase=Phase::Starting;}
        state.generation
    }
    pub fn issue(&self,desired:bool)->u64 {self.reserve(|_|desired)}
    pub fn preserve(&self,active:bool)->u64 {self.reserve(|state|if state.generation!=state.completed{state.desired}else{active})}
    pub fn toggle(&self,active:bool)->u64 {self.reserve(|state|if state.generation!=state.completed{!state.desired}else{!active})}
    #[cfg(any(test,feature="uiaccess-acceptance"))]
    pub fn starting(&self)->bool {self.state.lock().unwrap_or_else(|p|p.into_inner()).phase==Phase::Starting}
    pub fn desired(&self)->bool {self.state.lock().unwrap_or_else(|p|p.into_inner()).desired}
    pub fn complete(&self,id:u64){let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());if state.generation==id{state.completed=id;}}
    pub fn current(&self,id:u64)->bool {self.state.lock().unwrap_or_else(|p|p.into_inner()).generation==id}
    pub fn prepare(&self,id:u64,deadline:Instant)->Result<mpsc::Receiver<Result<(),String>>,String> {
        let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());
        if state.generation!=id||!state.desired||Instant::now()>=deadline{return Err("悬浮框启动已取消或超时".into());}
        let (send,receive)=mpsc::channel();state.owner=Some(id);state.phase=Phase::Starting;state.deadline=Some(deadline);state.layout=false;state.ready=Some(send);Ok(receive)
    }
    pub fn live(&self,id:u64)->bool {self.state.lock().unwrap_or_else(|p|p.into_inner()).owner==Some(id)}
    pub fn layout(&self,id:u64)->Result<(),String> {
        let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());
        if state.owner!=Some(id){return Err("旧竖排窗口布局已失效".into());}state.layout=true;Ok(())
    }
    pub fn ready(&self,id:u64,error:Option<String>)->Result<(),String> {
        let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());
        if state.owner!=Some(id){return Err("旧竖排窗口就绪消息已失效".into());}
        if state.phase==Phase::Running{return Ok(());}
        if state.generation!=id||!state.desired||state.deadline.is_none_or(|at|Instant::now()>=at){return Err("竖排启动已取消或超时".into());}
        if error.is_none()&&!state.layout{return Err("竖排首次布局尚未完成".into());}
        if let Some(send)=state.ready.take(){let _=send.send(error.map_or(Ok(()),Err));}Ok(())
    }
    /// Called on the owning UI thread for vertical show, without a cross-thread wait.
    pub fn commit<F>(&self,id:u64,show:F)->Result<(),String> where F:FnOnce()->Result<(),String> {
        let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());
        if state.generation!=id||!state.desired||state.deadline.is_some_and(|at|Instant::now()>=at){return Err("悬浮框启动已取消或超时".into());}
        show()?;state.phase=Phase::Running;state.deadline=None;Ok(())
    }
    pub fn keep_running(&self,id:u64)->Result<(),String>{self.commit(id,||Ok(()))}
    pub fn stop(&self,id:u64){let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());if state.generation==id{state.phase=Phase::Stopped;state.owner=None;state.ready=None;state.deadline=None;}}
    pub fn cancel(&self,id:u64)->Option<u64>{let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());if state.generation!=id{return None;}state.generation+=1;state.completed=state.generation;state.desired=false;state.phase=Phase::Stopped;state.owner=None;state.ready=None;state.deadline=None;Some(state.generation)}
    pub fn closed(&self,id:u64)->bool {
        let mut state=self.state.lock().unwrap_or_else(|p|p.into_inner());
        if state.owner!=Some(id){return false;}state.owner=None;state.ready=None;state.phase=Phase::Stopped;state.deadline=None;
        if state.generation==id{state.desired=false;}true
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn ready_requires_the_current_window_and_layout(){let life=Lifecycle::new();let id=life.issue(true);let receive=life.prepare(id,Instant::now()+START_BUDGET).unwrap();assert!(life.ready(id,None).is_err());life.layout(id).unwrap();life.ready(id,None).unwrap();assert!(receive.try_recv().unwrap().is_ok());life.commit(id,||Ok(())).unwrap();assert!(!life.starting());}
    #[test] fn timeout_prevents_late_show(){let life=Lifecycle::new();let id=life.issue(true);let _=life.prepare(id,Instant::now()+START_BUDGET).unwrap();let cleanup=life.cancel(id).unwrap();assert!(life.current(cleanup));let called=std::cell::Cell::new(false);assert!(life.commit(id,||{called.set(true);Ok(())}).is_err());assert!(!called.get());assert!(life.ready(id,None).is_err());let next=life.issue(true);assert!(life.cancel(id).is_none());assert!(!life.current(cleanup));assert!(life.current(next));}
    #[test] fn old_destroy_cannot_clear_a_new_window(){let life=Lifecycle::new();let old=life.issue(true);let _=life.prepare(old,Instant::now()+START_BUDGET).unwrap();let next=life.issue(true);let _=life.prepare(next,Instant::now()+START_BUDGET).unwrap();assert!(!life.closed(old));assert!(life.live(next));}
    #[test] fn settings_update_keeps_the_running_window_generation(){let life=Lifecycle::new();let id=life.issue(true);let _=life.prepare(id,Instant::now()+START_BUDGET).unwrap();life.commit(id,||Ok(())).unwrap();let update=life.issue(true);life.keep_running(update).unwrap();assert!(life.live(id));life.layout(id).unwrap();assert!(life.closed(id));}
    #[test] fn settings_cannot_resurrect_an_earlier_stop(){let life=Lifecycle::new();let _=life.issue(true);let _=life.issue(false);let next=life.preserve(true);assert!(life.current(next));assert!(!life.desired());}
    #[test] fn live_settings_are_not_limited_by_the_old_boot_deadline(){let life=Lifecycle::new();let id=life.issue(true);let _=life.prepare(id,Instant::now()+Duration::from_millis(50)).unwrap();life.commit(id,||Ok(())).unwrap();std::thread::sleep(Duration::from_millis(60));let update=life.issue(true);assert!(life.keep_running(update).is_ok());}
}
