#![allow(non_snake_case, non_camel_case_types, unused_unsafe, dead_code, unused_variables)]
// Only the external COM boundary is replaced. The creation block is copied from
// production by the runner, so changing its call order or fallback changes tests.
use std::{cell::RefCell, rc::Rc};

const E_NOINTERFACE: i32 = 0x80004002u32 as i32;
const E_FAIL: i32 = 0x80004005u32 as i32;
#[derive(Debug, Clone, Copy, PartialEq)]
struct Error(i32);
impl Error { fn code(&self) -> i32 { self.0 } }
type Result<T> = std::result::Result<T, Error>;
#[derive(Debug, Clone, Copy, PartialEq)]
struct COREWEBVIEW2_COLOR { R: u8, G: u8, B: u8, A: u8 }
#[derive(Debug, PartialEq)]
enum Call { Options, Color(COREWEBVIEW2_COLOR), WithOptions(usize), Legacy(usize) }
#[derive(Default)]
struct State {
    calls: RefCell<Vec<Call>>,
    env10_error: Option<i32>,
    opts3_error: Option<i32>,
    options_error: Option<i32>,
    color_error: Option<i32>,
    creation_error: Option<i32>,
}
trait Cast: Sized { fn query(state: Rc<State>) -> Result<Self>; }
struct Environment(Rc<State>);
struct Options(Rc<State>);
struct ICoreWebView2Environment10(Rc<State>);
struct ICoreWebView2Environment3(Rc<State>);
struct ICoreWebView2ControllerOptions3(Rc<State>);
impl Environment { fn cast<T: Cast>(&self) -> Result<T> { T::query(self.0.clone()) } }
impl Options { fn cast<T: Cast>(&self) -> Result<T> { T::query(self.0.clone()) } }
impl Cast for ICoreWebView2Environment10 {
    fn query(state: Rc<State>) -> Result<Self> {
        if let Some(code) = state.env10_error { Err(Error(code)) } else { Ok(Self(state)) }
    }
}
impl Cast for ICoreWebView2ControllerOptions3 {
    fn query(state: Rc<State>) -> Result<Self> {
        if let Some(code) = state.opts3_error { Err(Error(code)) } else { Ok(Self(state)) }
    }
}
impl Cast for ICoreWebView2Environment3 {
    fn query(state: Rc<State>) -> Result<Self> { Ok(Self(state)) }
}
impl ICoreWebView2Environment10 {
    fn CreateCoreWebView2ControllerOptions(&self) -> Result<Options> {
        self.0.calls.borrow_mut().push(Call::Options);
        if let Some(code) = self.0.options_error { Err(Error(code)) } else { Ok(Options(self.0.clone())) }
    }
    fn CreateCoreWebView2CompositionControllerWithOptions(&self, hwnd: usize, _: &Options, _: &()) -> Result<()> {
        self.0.calls.borrow_mut().push(Call::WithOptions(hwnd));
        self.0.creation_error.map_or(Ok(()), |code| Err(Error(code)))
    }
}
impl ICoreWebView2ControllerOptions3 {
    fn SetDefaultBackgroundColor(&self, color: COREWEBVIEW2_COLOR) -> Result<()> {
        self.0.calls.borrow_mut().push(Call::Color(color));
        self.0.color_error.map_or(Ok(()), |code| Err(Error(code)))
    }
}
impl ICoreWebView2Environment3 {
    fn CreateCoreWebView2CompositionController(&self, hwnd: usize, _: &()) -> Result<()> {
        self.0.calls.borrow_mut().push(Call::Legacy(hwnd));
        self.0.creation_error.map_or(Ok(()), |code| Err(Error(code)))
    }
}
fn create(state: Rc<State>, background_color: Option<(u8,u8,u8,u8)>) -> Result<()> {
    let env = Environment(state);
    let composition_hwnd = 0x1234;
    let handler = ();
    /* PRODUCTION_CREATION */
    Ok(())
}
fn transparent() -> Option<(u8,u8,u8,u8)> { Some((0,0,0,0)) }
fn color(r:u8,g:u8,b:u8,a:u8) -> Call { Call::Color(COREWEBVIEW2_COLOR{R:r,G:g,B:b,A:a}) }

#[test]
fn transparent_color_reaches_options_before_controller_creation() {
    let state = Rc::new(State::default());
    create(state.clone(), transparent()).unwrap();
    assert_eq!(*state.calls.borrow(), vec![Call::Options,color(0,0,0,0),Call::WithOptions(0x1234)],
        "Transparent background must be part of the composition creation request");
}
#[test]
fn nonzero_alpha_keeps_existing_opaque_api_semantics() {
    let state = Rc::new(State::default());
    create(state.clone(), Some((12,34,56,128))).unwrap();
    assert_eq!(*state.calls.borrow(), vec![Call::Options,color(12,34,56,255),Call::WithOptions(0x1234)]);
}
#[test]
fn no_explicit_background_keeps_legacy_creation() {
    let state = Rc::new(State::default());
    create(state.clone(), None).unwrap();
    assert_eq!(*state.calls.borrow(), vec![Call::Legacy(0x1234)]);
}
#[test]
fn old_environment_falls_back_to_original_creation() {
    let state = Rc::new(State{env10_error:Some(E_NOINTERFACE),..Default::default()});
    create(state.clone(), transparent()).unwrap();
    assert_eq!(*state.calls.borrow(), vec![Call::Legacy(0x1234)]);
}
#[test]
fn old_options_fall_back_without_claiming_creation_time_color() {
    let state = Rc::new(State{opts3_error:Some(E_NOINTERFACE),..Default::default()});
    create(state.clone(), transparent()).unwrap();
    assert_eq!(*state.calls.borrow(), vec![Call::Options,Call::Legacy(0x1234)]);
}
#[test]
fn environment_query_failure_is_not_compatibility_fallback() {
    let state = Rc::new(State{env10_error:Some(E_FAIL),..Default::default()});
    assert_eq!(create(state.clone(), transparent()), Err(Error(E_FAIL)));
    assert!(state.calls.borrow().is_empty());
}
#[test]
fn options_query_failure_is_not_compatibility_fallback() {
    let state = Rc::new(State{opts3_error:Some(E_FAIL),..Default::default()});
    assert_eq!(create(state.clone(), transparent()), Err(Error(E_FAIL)));
    assert_eq!(*state.calls.borrow(), vec![Call::Options]);
}
#[test]
fn option_creation_failure_stops_before_controller_creation() {
    let state = Rc::new(State{options_error:Some(E_FAIL),..Default::default()});
    assert_eq!(create(state.clone(), transparent()), Err(Error(E_FAIL)));
    assert_eq!(*state.calls.borrow(), vec![Call::Options]);
}
#[test]
fn color_failure_stops_before_controller_creation() {
    let state = Rc::new(State{color_error:Some(E_FAIL),..Default::default()});
    assert_eq!(create(state.clone(), transparent()), Err(Error(E_FAIL)));
    assert_eq!(*state.calls.borrow(), vec![Call::Options,color(0,0,0,0)]);
}
#[test]
fn controller_creation_failure_does_not_create_a_second_controller() {
    let state = Rc::new(State{creation_error:Some(E_FAIL),..Default::default()});
    assert_eq!(create(state.clone(), transparent()), Err(Error(E_FAIL)));
    assert_eq!(*state.calls.borrow(), vec![Call::Options,color(0,0,0,0),Call::WithOptions(0x1234)]);
}
