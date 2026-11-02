//! Physical-pixel placement. Only a manual move replaces the reference frame.
#[derive(Clone, Debug, PartialEq, serde::Serialize, serde::Deserialize)]
pub struct PositionReference {
    pub monitor: String,
    pub left: i32,
    pub top: i32,
    pub width: i32,
    pub height: i32,
    pub window_width: i32,
    pub window_height: i32,
}

fn axis(
    value: i32,
    origin: i32,
    extent: i32,
    size: i32,
    reference: Option<(i32, i32, i32)>,
) -> i32 {
    let available = (i64::from(extent) - i64::from(size)).max(0);
    let offset = match reference {
        Some((old_origin, old_extent, old_size)) if old_extent > old_size && old_size > 0 => {
            let fraction = ((f64::from(value) - f64::from(old_origin))
                / (f64::from(old_extent) - f64::from(old_size)))
            .clamp(0.0, 1.0);
            (fraction * available as f64).round() as i64
        }
        _ => (i64::from(value) - i64::from(origin)).clamp(0, available),
    };
    (i64::from(origin) + offset).clamp(i64::from(i32::MIN), i64::from(i32::MAX)) as i32
}

pub fn fit(
    point: (i32, i32),
    reference: Option<&PositionReference>,
    area: (i32, i32, i32, i32),
    size: (i32, i32),
) -> (i32, i32) {
    (
        axis(
            point.0,
            area.0,
            area.2,
            size.0,
            reference.map(|r| (r.left, r.width, r.window_width)),
        ),
        axis(
            point.1,
            area.1,
            area.3,
            size.1,
            reference.map(|r| (r.top, r.height, r.window_height)),
        ),
    )
}

/// Old settings have no reference. Adopt one only when their unmodified point
/// fits; a temporary smaller display must not erase an offscreen legacy point.
pub fn can_adopt(point: (i32, i32), area: (i32, i32, i32, i32), size: (i32, i32)) -> bool {
    size.0 > 0
        && size.1 > 0
        && size.0 < area.2
        && size.1 < area.3
        && fit(point, None, area, size) == point
}

#[cfg(test)]
mod tests {
    use super::*;
    fn reference() -> PositionReference {
        PositionReference {
            monitor: "display1".into(),
            left: 0,
            top: 0,
            width: 2560,
            height: 1440,
            window_width: 400,
            window_height: 120,
        }
    }
    #[test]
    fn resolution_and_aspect_ratio_roundtrip_always_uses_original_reference() {
        let r = reference();
        let point = (2100, 120);
        let results: Vec<_> = [(2560, 1440), (1920, 1080), (1280, 960), (2560, 1440)]
            .into_iter()
            .map(|(w, h)| fit(point, Some(&r), (0, 0, w, h), (400, 120)))
            .collect();
        assert_eq!(
            results,
            vec![(2100, 120), (1478, 87), (856, 76), (2100, 120)]
        );
        assert_eq!(r, reference());
    }
    #[test]
    fn interior_and_edges_preserve_fraction_of_available_space() {
        let r = reference();
        assert_eq!(
            fit((1080, 660), Some(&r), (0, 0, 1920, 1080), (400, 120)),
            (760, 480)
        );
        assert_eq!(
            fit((2160, 1320), Some(&r), (0, 0, 1280, 960), (400, 120)),
            (880, 840)
        );
        assert_eq!(fit((0, 0), Some(&r), (0, 0, 1280, 960), (400, 120)), (0, 0));
    }
    #[test]
    fn negative_monitor_origin_and_dpi_scaled_window_are_physical() {
        let r = PositionReference {
            left: -2560,
            top: -200,
            ..reference()
        };
        assert_eq!(
            fit(
                (-1480, 460),
                Some(&r),
                (-1920, -100, 1920, 1080),
                (600, 180)
            ),
            (-1260, 350)
        );
    }
    #[test]
    fn dpi_125_and_150_percent_preserve_center_and_restore_original_pixels() {
        let r = reference();
        for (size, expected) in [((500, 150), (1030, 645)), ((600, 180), (980, 630))] {
            assert_eq!(
                fit((1080, 660), Some(&r), (0, 0, 2560, 1440), size),
                expected
            );
            assert_eq!(
                fit((1080, 660), Some(&r), (0, 0, 2560, 1440), (400, 120)),
                (1080, 660)
            );
        }
    }
    #[test]
    fn small_display_or_invalid_reference_is_bounded() {
        let r = reference();
        assert_eq!(
            fit((2100, 120), Some(&r), (10, 20, 200, 80), (400, 120)),
            (10, 20)
        );
        let invalid = PositionReference {
            width: 0,
            height: 0,
            ..r
        };
        assert_eq!(
            fit((2100, -100), Some(&invalid), (0, 0, 1920, 1080), (400, 120)),
            (1520, 0)
        );
    }
    #[test]
    fn legacy_offscreen_coordinates_are_not_adopted_in_smaller_mode() {
        assert!(!can_adopt((2100, 120), (0, 0, 1920, 1080), (400, 120)));
        assert!(can_adopt((2100, 120), (0, 0, 2560, 1440), (400, 120)));
        assert_eq!(
            fit((2100, 120), None, (0, 0, 2560, 1440), (400, 120)),
            (2100, 120)
        );
    }
}
