use super::control::Step;
use std::io::{self, Write};

const MAX_RECORDS: usize = 4_000_000;

pub fn capacity_for(steps: &[Step]) -> Result<usize, String> {
    let total = steps
        .iter()
        .try_fold(1024u64, |sum, step| {
            let rate = if step.fps == 0 {
                10_000
            } else {
                u64::from(step.fps) * 2 + 1000
            };
            sum.checked_add(rate.checked_mul(step.seconds)?)
        })
        .ok_or("frame CSV capacity overflow")?;
    let capacity = usize::try_from(total).map_err(|_| "frame CSV capacity overflow")?;
    if capacity > MAX_RECORDS {
        return Err(format!(
            "frame CSV needs {capacity} records; limit is {MAX_RECORDS}; shorten the run"
        ));
    }
    Ok(capacity)
}

#[derive(Clone, Copy, Default)]
struct Frame {
    start: i64,
    end: i64,
    hr: i32,
    phase: usize,
    target: u32,
    state_verified: bool,
}

pub struct Capture {
    frames: Vec<Frame>,
    used: usize,
    frequency: i64,
    origin: i64,
}

impl Capture {
    pub fn with_capacity(capacity: usize) -> Result<Self, String> {
        let mut frames = Vec::new();
        frames
            .try_reserve_exact(capacity)
            .map_err(|e| format!("frame CSV allocation failed: {e}"))?;
        // Initialize every record before measurement, avoiding first-touch allocation in Present.
        frames.resize(capacity, Frame::default());
        Ok(Self {
            frames,
            used: 0,
            frequency: 0,
            origin: 0,
        })
    }

    pub fn begin(&mut self, frequency: i64, origin: i64) {
        self.frequency = frequency;
        self.origin = origin;
    }

    pub fn present(
        &mut self,
        tag: (usize, u32),
        mut clock: impl FnMut() -> i64,
        call: impl FnOnce() -> i32,
    ) -> Result<i32, String> {
        let frame = self
            .frames
            .get_mut(self.used)
            .ok_or("frame CSV buffer full; no unrecorded Present was issued")?;
        let start = clock();
        let hr = call();
        let end = clock();
        *frame = Frame {
            start,
            end,
            hr,
            phase: tag.0,
            target: tag.1,
            state_verified: false,
        };
        self.used += 1;
        Ok(hr)
    }

    pub fn verify_last(&mut self, valid: bool) {
        if self.used > 0 {
            self.frames[self.used - 1].state_verified = valid;
        }
    }

    pub fn len(&self) -> usize {
        self.used
    }

    pub fn write_csv(&self, writer: &mut impl Write, pid: u32, valid: bool) -> io::Result<()> {
        writeln!(writer, "pid,frame,phase,target_fps,qpc_frequency,measurement_start_qpc,present_start_qpc,present_end_qpc,present_start_seconds,present_end_seconds,hresult,present_s_ok,state_verified,run_valid")?;
        for (index, frame) in self.frames[..self.used].iter().enumerate() {
            writeln!(
                writer,
                "{pid},{},{},{},{},{},{},{},{:.9},{:.9},0x{:08x},{},{},{valid}",
                index + 1,
                frame.phase,
                frame.target,
                self.frequency,
                self.origin,
                frame.start,
                frame.end,
                (frame.start - self.origin) as f64 / self.frequency as f64,
                (frame.end - self.origin) as f64 / self.frequency as f64,
                frame.hr as u32,
                frame.hr == 0,
                frame.state_verified
            )?;
        }
        writer.flush()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::Cell;

    #[derive(Default)]
    struct Sink {
        bytes: Vec<u8>,
        flushes: usize,
    }
    impl Write for Sink {
        fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
            self.bytes.extend_from_slice(bytes);
            Ok(bytes.len())
        }
        fn flush(&mut self) -> io::Result<()> {
            self.flushes += 1;
            Ok(())
        }
    }

    #[test]
    fn brackets_only_the_real_present_call() {
        let mut capture = Capture::with_capacity(2).unwrap();
        capture.begin(1000, 100);
        let calls = Cell::new(0);
        let ticks = Cell::new(0);
        let hr = capture
            .present(
                (1, 300),
                || {
                    let tick = ticks.get();
                    assert_eq!(calls.get(), tick);
                    ticks.set(tick + 1);
                    110 + 5 * tick
                },
                || {
                    calls.set(1);
                    0
                },
            )
            .unwrap();
        assert_eq!(hr, 0);
        assert_eq!(ticks.get(), 2);
        assert_eq!(capture.used, 1);
        assert_eq!((capture.frames[0].start, capture.frames[0].end), (110, 115));
    }

    #[test]
    fn retains_failed_and_occluded_results_without_fabricating_success() {
        let mut capture = Capture::with_capacity(3).unwrap();
        capture.begin(1000, 100);
        for hr in [0, 0x087a0001, 0x887a0005u32 as i32] {
            assert_eq!(capture.present((2, 60), || 110, || hr).unwrap(), hr);
            capture.verify_last(hr == 0);
        }
        let mut sink = Sink::default();
        capture.write_csv(&mut sink, 42, false).unwrap();
        let text = String::from_utf8(sink.bytes).unwrap();
        let rows: Vec<_> = text.lines().skip(1).collect();
        assert_eq!(rows.len(), 3);
        assert!(rows[0].ends_with(",0x00000000,true,true,false"));
        assert!(rows[1].ends_with(",0x087a0001,false,false,false"));
        assert!(rows[2].ends_with(",0x887a0005,false,false,false"));
        assert_eq!(sink.flushes, 1);
    }

    #[test]
    fn csv_preserves_raw_ticks_and_phase_for_independent_window_selection() {
        let mut capture = Capture::with_capacity(1).unwrap();
        capture.begin(1000, 100);
        let mut clock = [350, 355].into_iter();
        capture
            .present((3, 300), || clock.next().unwrap(), || 0)
            .unwrap();
        capture.verify_last(true);
        let mut sink = Sink::default();
        capture.write_csv(&mut sink, 42, true).unwrap();
        let text = String::from_utf8(sink.bytes).unwrap();
        assert_eq!(
            text.lines().nth(1).unwrap(),
            "42,1,3,300,1000,100,350,355,0.250000000,0.255000000,0x00000000,true,true,true"
        );
        assert_eq!(sink.flushes, 1);
    }

    #[test]
    fn full_buffer_never_reallocates_or_calls_an_unrecorded_present() {
        let mut capture = Capture::with_capacity(1).unwrap();
        let address = capture.frames.as_ptr();
        capture.present((1, 300), || 10, || 0).unwrap();
        let called = Cell::new(false);
        assert!(capture
            .present(
                (1, 300),
                || 20,
                || {
                    called.set(true);
                    0
                }
            )
            .is_err());
        assert!(!called.get());
        assert_eq!(capture.used, 1);
        assert_eq!(capture.frames.as_ptr(), address);
    }

    #[test]
    fn csv_propagates_write_and_flush_failures() {
        struct Broken(bool);
        impl Write for Broken {
            fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
                if self.0 {
                    Ok(bytes.len())
                } else {
                    Err(io::Error::other("write failed"))
                }
            }
            fn flush(&mut self) -> io::Result<()> {
                Err(io::Error::other("flush failed"))
            }
        }
        let capture = Capture::with_capacity(0).unwrap();
        assert!(capture.write_csv(&mut Broken(false), 42, false).is_err());
        assert!(capture.write_csv(&mut Broken(true), 42, false).is_err());
    }

    #[test]
    fn capacity_covers_requested_step_run_and_rejects_oversized_requests() {
        let steps = [
            Step {
                fps: 300,
                seconds: 12,
            },
            Step {
                fps: 60,
                seconds: 12,
            },
            Step {
                fps: 300,
                seconds: 35,
            },
        ];
        assert_eq!(capacity_for(&steps).unwrap(), 89_664);
        assert!(capacity_for(&[Step {
            fps: 600,
            seconds: u64::MAX
        }])
        .is_err());
        assert!(capacity_for(&[Step {
            fps: 600,
            seconds: 100_000
        }])
        .is_err());
        assert_eq!(
            capacity_for(&[Step { fps: 0, seconds: 1 }]).unwrap(),
            11_024
        );
    }
}
