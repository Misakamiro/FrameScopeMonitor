use super::*;

#[test]
fn osd_transparent_background_does_not_become_opaque_black_text() {
    let mut pixels = [0x00000001, 0x00000000, 0x00ffffff, 0x40040404, 0xff111111];
    repair_gdi_text_alpha(&mut pixels);
    assert_eq!(pixels, [0, 0xff000000, 0xffffffff, 0x40040404, 0xff111111]);
    let mut blank = vec![0x00000001; 400];
    repair_gdi_text_alpha(&mut blank);
    assert!(blank.iter().all(|pixel| *pixel == 0));
}

fn all_items_settings(font_size: u32) -> crate::overlay_panel::OverlaySettings {
    let mut settings = crate::overlay_panel::OverlaySettings::default();
    settings.font_size = font_size;
    for item in &mut settings.display_items {
        item.enabled = true;
    }
    settings
}

fn area(width: i32, height: i32) -> RECT {
    RECT { left: 0, top: 0, right: width, bottom: height }
}

fn measured_layout(
    settings: &crate::overlay_panel::OverlaySettings,
    data: &crate::overlay_panel::OverlayHardwareData,
    dpi: f32, island: bool, area: RECT,
) -> Result<ItemLayout, String> {
    unsafe {
        let dc = CreateCompatibleDC(ptr::null_mut());
        assert!(!dc.is_null());
        let font = create_compatible_font(dpi, &settings.font, settings.font_size);
        assert!(!font.is_null());
        let old_font = SelectObject(dc, font as _);
        let mut actual_font: LOGFONTW = std::mem::zeroed();
        assert_ne!(GetObjectW(font as _, std::mem::size_of::<LOGFONTW>() as i32,
            &mut actual_font as *mut _ as _), 0);
        assert_eq!(actual_font.lfHeight, -(settings.font_size as f32 * dpi).round() as i32);
        let mut token = 0;
        let mut graphics = ptr::null_mut();
        let mut gp_font = ptr::null_mut();
        let mut format = ptr::null_mut();
        if island {
            let input = GdiplusStartupInput { GdiplusVersion: 1, DebugEventCallback: 0,
                SuppressBackgroundThread: 0, SuppressExternalCodecs: 0 };
            assert_eq!(GdiplusStartup(&mut token, &input, ptr::null_mut()), 0);
            assert_eq!(GdipCreateFromHDC(dc, &mut graphics), 0);
            assert_eq!(GdipCreateFontFromDC(dc, &mut gp_font), 0);
            assert_eq!(GdipCreateStringFormat(StringFormatFlagsNoWrap as i32, 0, &mut format), 0);
            GdipSetTextRenderingHint(graphics, TextRenderingHintAntiAliasGridFit);
        }
        let measure = |text: &str| {
            if island { measure_gdiplus_text(graphics, gp_font, format, text) }
            else { measure_text_width(dc, font, text) }
        };
        let mut items = build_display_items(settings, data);
        let expected: String = items.iter().map(|item| format!("{}{}", item.label, item.value)).collect();
        let original_count = items.len();
        let gap = (10.0 * dpi) as i32;
        for item in &mut items {
            item.label_width = measure(&item.label);
            item.value_width = measure(&item.value);
            item.total_width = item.label_width + item.value_width + if item.label.is_empty() { 0 } else { gap };
        }
        let original_width = items.iter().map(|item| item.total_width).sum::<i32>()
            + items.len().saturating_sub(1) as i32 * (16.0 * dpi) as i32 + (32.0 * dpi) as i32;
        let row_height = measured_row_height(dc, font, dpi, island);
        let result = layout_measured_items(items, area, dpi, row_height, 0x00ffffff, measure);
        match &result {
            Ok(layout) => {
                let actual: String = layout.items.iter().map(|item| format!("{}{}", item.label, item.value)).collect();
                assert_eq!(actual, expected, "all labels and values must survive in order");
                assert_inside(layout, area);
                if island {
                    for item in &layout.items {
                        for (text, width) in [(&item.label, item.label_width), (&item.value, item.value_width)] {
                            if text.is_empty() { continue; }
                            let wide: Vec<u16> = text.encode_utf16().collect();
                            let rect = RectF { X: 0.0, Y: 0.0, Width: width as f32, Height: row_height as f32 };
                            let mut bounds: RectF = std::mem::zeroed();
                            let mut fitted = 0;
                            let mut lines = 0;
                            assert_eq!(GdipMeasureString(graphics, wide.as_ptr(), wide.len() as i32,
                                gp_font, &rect, format, &mut bounds, &mut fitted, &mut lines), 0);
                            assert_eq!(fitted, wide.len() as i32, "GDI+ text must fit the actual draw rectangle: {text}");
                            assert_eq!(lines, 1);
                        }
                    }
                }
                println!("engine={} font={} dpi={} items={} old_width={} work={}x{} layout={}x{} rows={}",
                    if island { "GDI+" } else { "GDI" }, settings.font_size, dpi, original_count,
                    original_width, area.right-area.left, area.bottom-area.top, layout.width, layout.height,
                    layout.height / layout.row_height);
            }
            Err(error) => println!("engine={} font={} dpi={} old_width={} error={error}",
                if island { "GDI+" } else { "GDI" }, settings.font_size, dpi, original_width),
        }
        if island {
            GdipDeleteStringFormat(format);
            GdipDeleteFont(gp_font);
            GdipDeleteGraphics(graphics);
            GdiplusShutdown(token);
        }
        SelectObject(dc, old_font);
        DeleteObject(font as _);
        DeleteDC(dc);
        result
    }
}

fn assert_inside(layout: &ItemLayout, area: RECT) {
    assert!(layout.width > 0 && layout.width <= area.right - area.left);
    assert!(layout.height > 0 && layout.height <= area.bottom - area.top);
    assert_eq!(layout.items.len(), layout.positions.len());
    for (index, (item, &(x, y))) in layout.items.iter().zip(&layout.positions).enumerate() {
        assert!(x >= 0 && y >= 0);
        assert!(x + item.total_width <= layout.width);
        assert!(y + layout.row_height <= layout.height);
        if index > 0 {
            let previous = &layout.items[index-1];
            let (px, py) = layout.positions[index-1];
            assert!(y >= py + layout.row_height || y == py && x >= px + previous.total_width);
        }
    }
}

fn measured_width(settings: &crate::overlay_panel::OverlaySettings) -> i32 {
    measured_layout(settings, &Default::default(), 1.0, false, area(1248, 720)).unwrap().width
}

#[test]
fn osd_layout_all_metrics_fit_small_monitor() {
    assert!(measured_width(&all_items_settings(13)) <= 1248);
}

#[test]
fn osd_layout_font_40_fits_small_monitor() {
    assert!(measured_width(&all_items_settings(40)) <= 1248);
}

#[test]
fn osd_layout_single_wide_item_fits_without_dropping_text() {
    let mut settings = all_items_settings(40);
    settings.display_items.clear();
    settings.custom_items.push(crate::overlay_panel::CustomOverlayItem {
        id: "wide".into(),
        text: "CPU 12345MHz GPU 12345MHz MEMORY 100% ".repeat(3),
        color: "#ffffff".into(),
        enabled: true,
    });
    assert!(measured_width(&settings) <= 1248);
}

#[test]
fn osd_layout_both_engines_all_metrics_font_dpi_small_screen_matrix() {
    let data = crate::overlay_panel::OverlayHardwareData {
        fps: Some(600), fps_1low: Some(350.0), fps_01low: Some(200.0),
        cpu_usage: Some(100), cpu_temp: Some(100.0), cpu_clock: Some(5600),
        cpu_fan_speed: Some(2800), cpu_voltage: Some(1.375), cpu_power: Some(250.0),
        gpu_usage: Some(100), gpu_temp: Some(95.0), gpu_clock: Some(2800),
        gpu_fan_speed: Some(3500), gpu_voltage: Some(1.050), gpu_power: Some(450),
        gpu_memory_clock: Some(12000), gpu_vram_used: Some(16), gpu_vram_total: Some(24),
        memory_usage: Some(100.0), ssd_temp: Some(75.0), game_ping: Some(120),
        delta_password: Some("1234 5678 9012".into()), ..Default::default()
    };
    for island in [false, true] {
        for font_size in [13, 40] {
            for (dpi, width, height) in [(1.0, 800, 560), (1.0, 1248, 720), (1.5, 1920, 1080), (2.0, 2560, 1440)] {
                let layout = measured_layout(&all_items_settings(font_size), &data, dpi, island, area(width, height)).unwrap();
                assert_eq!(layout.items.len(), 22, "normal metrics must remain whole");
                assert!(layout.height > layout.row_height);
            }
        }
    }
}

#[test]
fn osd_layout_oversized_unicode_item_keeps_all_characters() {
    let mut settings = all_items_settings(40);
    settings.display_items.clear();
    settings.custom_items.push(crate::overlay_panel::CustomOverlayItem {
        id: "unicode".into(), text: "\u{4e2d}\u{6587} GPU \u{1f680} 1234 ".repeat(4),
        color: "#12ab34".into(), enabled: true,
    });
    for island in [false, true] {
        let layout = measured_layout(&settings, &Default::default(), 1.0, island, area(320, 900)).unwrap();
        assert!(layout.items.len() > 1);
        assert!(layout.items.iter().all(|item| item.custom_color == Some(parse_hex_color("#12ab34"))));
    }
}

fn fake_item(label: &str, value: &str) -> DisplayItem {
    DisplayItem {
        label: label.into(), value: value.into(),
        label_width: label.len() as i32 * 10, value_width: value.len() as i32 * 10,
        total_width: (label.len() + value.len()) as i32 * 10 + if label.is_empty() { 0 } else { 10 },
        custom_color: None,
    }
}

#[test]
fn osd_layout_exact_fit_then_whole_metric_wraps() {
    let layout = layout_measured_items(vec![fake_item("A", "12"), fake_item("B", "34"), fake_item("C", "56")],
        area(128, 100), 1.0, 28, 0xffffff, |s| s.len() as i32 * 10).unwrap();
    assert_eq!(layout.positions, [(16, 0), (72, 0), (16, 28)]);
    assert_eq!((layout.width, layout.height), (128, 56));
}

#[test]
fn osd_layout_wide_label_and_value_keep_order_and_color() {
    let original = fake_item("LONG_LABEL", "100%");
    let layout = layout_measured_items(vec![original], area(82, 300), 1.0, 28, 0xffffff,
        |s| s.len() as i32 * 10).unwrap();
    let text: String = layout.items.iter().map(|item| format!("{}{}", item.label, item.value)).collect();
    assert_eq!(text, "LONG_LABEL100%");
    assert!(layout.items.iter().all(|item| item.custom_color == Some(0x000000ff)));
    assert_inside(&layout, area(82, 300));
}

#[test]
fn osd_layout_impossible_height_or_glyph_is_explicit_error_not_clipped_success() {
    assert!(layout_measured_items(vec![fake_item("", "123456")], area(62, 28), 1.0, 28,
        0xffffff, |s| s.len() as i32 * 10).err().unwrap().contains("height"));
    assert!(layout_measured_items(vec![fake_item("", "1")], area(33, 100), 1.0, 28,
        0xffffff, |s| s.len() as i32 * 10).is_err());
    assert!(layout_measured_items(vec![], area(16, 100), 1.0, 28,
        0xffffff, |s| s.len() as i32 * 10).is_err());
}

#[test]
fn osd_layout_empty_has_bounded_nonzero_bitmap() {
    let layout = layout_measured_items(vec![], area(320, 200), 1.0, 28,
        0xffffff, |s| s.len() as i32 * 10).unwrap();
    assert_eq!((layout.width, layout.height), (32, 28));
}

#[test]
fn osd_layout_negative_monitor_origin_and_drag_position_are_physical_pixels() {
    let area = RECT { left: -1280, top: -200, right: 0, bottom: 800 };
    let mut settings = all_items_settings(40);
    settings.position_x = Some(-300);
    settings.position_y = Some(780);
    let point = fitted_position(&settings, area, 900, 250);
    assert_eq!((point.x, point.y), (-900, 550));
    settings.position_x = Some(-1100);
    settings.position_y = Some(-100);
    let point = fitted_position(&settings, area, 900, 250);
    assert_eq!((point.x, point.y), (-1100, -100), "valid drag coordinates must not be scaled");
    settings.position_x = None;
    settings.position_y = None;
    let point = fitted_position(&settings, area, 900, 250);
    assert_eq!((point.x, point.y), (-1090, -200));
}

#[test]
fn osd_layout_removed_monitor_saved_position_clamps_to_target_work_area() {
    let mut settings = all_items_settings(13);
    settings.position_x = Some(4000);
    settings.position_y = Some(-900);
    let point = fitted_position(&settings, area(800, 560), 780, 200);
    assert_eq!((point.x, point.y), (20, 0));
}

#[test]
fn osd_layout_drag_messages_gate_repaint_without_creating_a_window() {
    unsafe {
        assert!(!super::super::DRAG_MODE.load(super::super::Ordering::SeqCst));
        window_proc(ptr::null_mut(), WM_ENTERSIZEMOVE, 0, 0);
        assert!(IN_SIZE_MOVE.load(super::super::Ordering::SeqCst));
        // This must return before touching HWND, settings, DCs or position.
        repaint_overlay(ptr::null_mut());
        window_proc(ptr::null_mut(), WM_EXITSIZEMOVE, 0, 0);
        assert!(!IN_SIZE_MOVE.load(super::super::Ordering::SeqCst));
    }
}

#[test]
fn manual_reference_survives_rendering_resolution_and_style_changes() {
    for style in ["default","dynamic_island"] {
        let mut settings=crate::overlay_panel::OverlaySettings::default();
        settings.style=style.into(); settings.position_x=Some(2100); settings.position_y=Some(120);
        settings.position_reference=Some(crate::overlay_position::PositionReference{monitor:"test".into(),left:0,top:0,width:2560,height:1440,window_width:400,window_height:120});
        let expected=[(2100,120),(1478,87),(856,76),(2100,120)];
        for ((w,h),(x,y)) in [(2560,1440),(1920,1080),(1280,960),(2560,1440)].into_iter().zip(expected) {
            let point=fitted_position(&settings,area(w,h),400,120);
            assert_eq!((point.x,point.y),(x,y),"{style}");
            remember_position_reference(&settings,area(w,h),400,120);
            assert_eq!((settings.position_x,settings.position_y),(Some(2100),Some(120)));
        }
    }
}

#[test]
fn legacy_render_adopts_a_reference_without_overwriting_saved_coordinates() {
    let _guard=crate::overlay_panel::POSITION_TEST_LOCK.lock().unwrap();
    let before=crate::overlay_panel::CURRENT_SETTINGS.lock().unwrap().clone();
    let display=crate::overlay_display::resolve((0,0),None);
    let mut settings=crate::overlay_panel::OverlaySettings::default();
    settings.position_x=Some(display.area.left+100); settings.position_y=Some(display.area.top+100);
    *crate::overlay_panel::CURRENT_SETTINGS.lock().unwrap()=Some(settings.clone());
    remember_position_reference(&settings,display.area,200,100);
    let saved=crate::overlay_panel::get_or_init_settings();
    assert!(saved.position_reference.is_some());
    assert_eq!(saved.position_x,settings.position_x);
    assert_eq!(saved.position_y,settings.position_y);
    let tiny=RECT{left:display.area.left,top:display.area.top,right:display.area.left+220,bottom:display.area.top+120};
    let fitted=fitted_position(&saved,tiny,200,100);
    remember_position_reference(&saved,tiny,200,100);
    assert_eq!(crate::overlay_panel::get_or_init_settings().position_reference,saved.position_reference);
    assert_eq!(crate::overlay_panel::get_or_init_settings().position_x,settings.position_x);
    assert!(fitted.x<=tiny.right-200);
    *crate::overlay_panel::CURRENT_SETTINGS.lock().unwrap()=Some(settings.clone());
    remember_position_reference(&settings,tiny,200,100);
    assert!(crate::overlay_panel::get_or_init_settings().position_reference.is_none());
    assert_eq!(crate::overlay_panel::get_or_init_settings().position_x,settings.position_x);
    *crate::overlay_panel::CURRENT_SETTINGS.lock().unwrap()=before;
}
