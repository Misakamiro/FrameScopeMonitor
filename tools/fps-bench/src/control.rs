use std::time::Duration;

#[derive(Debug, PartialEq)]
pub struct Step {
    pub fps: u32,
    pub seconds: u64,
}

pub fn parse_steps(text: &str) -> Result<Vec<Step>, String> {
    let mut total = 0u64;
    text.split(',')
        .map(|part| {
            let (fps, seconds) = part.split_once(':').ok_or("expected FPS:SECONDS")?;
            let fps = fps.parse().map_err(|_| "invalid FPS")?;
            let seconds = seconds.parse().map_err(|_| "invalid duration")?;
            if seconds == 0 {
                return Err("duration must be positive".into());
            }
            total = total.checked_add(seconds).ok_or("duration overflow")?;
            Ok(Step { fps, seconds })
        })
        .collect()
}

pub fn step_at(steps: &[Step], elapsed: Duration) -> Option<usize> {
    let mut end = 0u64;
    steps.iter().position(|step| {
        end += step.seconds;
        elapsed.as_secs() < end
    })
}

#[derive(Default)]
pub struct StartGate {
    stable_since: Option<Duration>,
    armed: bool,
}

impl StartGate {
    pub fn ready(&mut self, now: Duration, focused: bool, space: bool, manual: bool) -> bool {
        if !focused {
            self.stable_since = None;
            self.armed = false;
            return false;
        }
        self.armed |= space || !manual;
        if !self.armed {
            return false;
        }
        let since = *self.stable_since.get_or_insert(now);
        now.saturating_sub(since) >= Duration::from_secs(2)
    }
}

pub fn tearing_flags(supported: bool, exclusive: bool) -> (u32, u32) {
    if supported && !exclusive {
        (0x800, 0x200)
    } else {
        (0, 0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_explicit_steps() {
        assert_eq!(
            parse_steps("300:10,60:5,300:10").unwrap(),
            [
                Step {
                    fps: 300,
                    seconds: 10
                },
                Step {
                    fps: 60,
                    seconds: 5
                },
                Step {
                    fps: 300,
                    seconds: 10
                }
            ]
        );
    }

    #[test]
    fn rejects_invalid_or_unbounded_steps() {
        for text in [
            "",
            "300",
            "300:0",
            "x:10",
            "300:1,",
            "300:18446744073709551615,60:1",
        ] {
            assert!(parse_steps(text).is_err(), "{text}");
        }
    }

    #[test]
    fn step_boundaries_are_not_smeared() {
        let steps = [
            Step {
                fps: 300,
                seconds: 10,
            },
            Step {
                fps: 60,
                seconds: 10,
            },
            Step {
                fps: 300,
                seconds: 10,
            },
        ];
        assert_eq!(step_at(&steps, Duration::from_millis(9999)), Some(0));
        assert_eq!(step_at(&steps, Duration::from_secs(10)), Some(1));
        assert_eq!(step_at(&steps, Duration::from_secs(20)), Some(2));
        assert_eq!(step_at(&steps, Duration::from_secs(30)), None);
    }

    #[test]
    fn automatic_start_requires_two_continuous_seconds() {
        let mut gate = StartGate::default();
        assert!(!gate.ready(Duration::ZERO, true, false, false));
        assert!(!gate.ready(Duration::from_millis(1999), true, false, false));
        assert!(gate.ready(Duration::from_secs(2), true, false, false));
    }

    #[test]
    fn manual_start_requires_space_then_two_seconds() {
        let mut gate = StartGate::default();
        assert!(!gate.ready(Duration::ZERO, true, false, true));
        assert!(!gate.ready(Duration::from_secs(20), true, false, true));
        assert!(!gate.ready(Duration::from_secs(21), true, true, true));
        assert!(!gate.ready(Duration::from_secs(22), true, false, true));
        assert!(gate.ready(Duration::from_secs(23), true, false, true));
    }

    #[test]
    fn losing_focus_disarms_manual_start() {
        let mut gate = StartGate::default();
        gate.ready(Duration::ZERO, true, true, true);
        assert!(!gate.ready(Duration::from_secs(1), false, false, true));
        assert!(!gate.ready(Duration::from_secs(2), true, false, true));
        assert!(!gate.ready(Duration::from_secs(20), true, false, true));
        assert!(!gate.ready(Duration::from_secs(21), true, true, true));
        assert!(gate.ready(Duration::from_secs(23), true, false, true));
    }

    #[test]
    fn automatic_focus_loss_resets_stability() {
        let mut gate = StartGate::default();
        gate.ready(Duration::ZERO, true, false, false);
        gate.ready(Duration::from_secs(1), false, false, false);
        assert!(!gate.ready(Duration::from_secs(2), true, false, false));
        assert!(!gate.ready(Duration::from_secs(3), true, false, false));
        assert!(gate.ready(Duration::from_secs(4), true, false, false));
    }

    #[test]
    fn tearing_is_only_enabled_for_supported_windowed_swapchains() {
        assert_eq!(tearing_flags(true, false), (0x800, 0x200));
        assert_eq!(tearing_flags(true, true), (0, 0));
        assert_eq!(tearing_flags(false, false), (0, 0));
        assert_eq!(tearing_flags(false, true), (0, 0));
    }
}
