#[derive(Default)]
pub struct State { visible:bool,minimized:bool,revision:u64 }
impl State {
    pub const fn new()->Self { Self {visible:false,minimized:false,revision:0} }
    pub fn update(&mut self,visible:bool,minimized:bool)->(bool,bool,u64) {
        if self.visible!=visible||self.minimized!=minimized {
            self.visible=visible;self.minimized=minimized;self.revision+=1;
        }
        (self.visible,self.minimized,self.revision)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn native_visibility_is_independent_of_keyboard_focus() {
        let mut state=State::default();
        assert_eq!(state.update(true,false),(true,false,1));
        assert_eq!(state.update(true,false),(true,false,1));
        assert_eq!(state.update(true,true),(true,true,2));
        assert_eq!(state.update(false,false),(false,false,3));
        assert_eq!(state.update(true,false),(true,false,4));
    }
}
