// Adapted from cpuzeez 26d9f0e204085b91d590fd5ec2c0625ed0020e39, MIT OR Apache-2.0.
// Copyright joaoregis and contributors. See resources/licenses.
//! Intel integrated memory controller (MCHBAR) decode, one table per register
//! layout. Every offset and bit range is from Intel's public datasheets:
//!
//! - SKL: Skylake..Comet Lake client. 6th Gen S-Platform Datasheet Vol. 2
//!   (332688-003) ch. 7, same layout in the 8th/9th Gen Vol. 2 (337345-002)
//!   ch. 7: channel 0 at 4000h, channel 1 at 4400h; MAD at 5000h; SA_PERF_STATUS 5918h.
//! - ICL: Ice Lake client. 10th Gen Datasheet Vol. 2 (341078-004) sec. 3.2/3.3:
//!   same channel blocks, 64-bit SC_GS_CFG at 4088h, Gear 2, new TC_PRE/TC_ODT.
//! - ADL: Alder/Raptor Lake. 12th Gen Datasheet Vol. 2 (EDC 767625/767626) and
//!   13th Gen Vol. 2 (EDC 767624), "MCHBAR Memory Controller Registers": MC0
//!   ch0 E000h, ch1 +800h, MC1 +10000h; shared MAD at D800h (+10000h for MC1).
//! - ARL: Arrow Lake. Core Ultra 200S/200HX CFG & MEM Registers (EDC 835538)
//!   and 200H/200U (EDC 844345): ADL channel map, tRCD moved to TC_ACT E138h,
//!   CL in TC_CAS E070h, TC_RFTP at E4A0h, Gear 2/4 only.
//!
//! Decode is pure (register reader closure in, values out) so it's tested
//! with values built from those bit layouts.

use super::Timings;

/// Bit field `hi:lo` of the register at `off`; 64-bit registers are read as
/// the dword holding the field (no field straddles a dword).
#[derive(Clone, Copy, Debug)]
pub struct F {
    off: u32,
    lo: u8,
    bits: u8,
}

const fn f(off: u32, hi: u8, lo: u8) -> F {
    F { off: off + (lo as u32 / 32) * 4, lo: lo % 32, bits: hi - lo + 1 }
}

impl F {
    fn get(self, v: u32) -> u32 {
        ((v as u64 >> self.lo) & ((1u64 << self.bits) - 1)) as u32
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Gear {
    /// SKL: SA_PERF_STATUS QCLK ratio already is the DRAM clock (MC_BIOS_REQ table: n * 133.33 = DCLK).
    None,
    /// ICL: SC_GS_CFG bit 31 Gear2, bit 30 no_gear2_param_divide.
    G2,
    /// ADL: bit 31 Gear2 (+30 no-divide), bit 15 Gear4 (+16 no-divide).
    G2G4,
    /// ARL: bit 31 gear_mode 0 = Gear2, 1 = Gear4; same no-divide bits.
    Mode,
}

pub struct Layout {
    /// (timing block base, MAD_DIMM register) per channel, probe order.
    chans: &'static [(u32, u32)],
    /// DIMM L / DIMM S size fields of MAD_DIMM (any non-zero = populated).
    dimm_l: F,
    dimm_s: F,
    /// MAD_INTER_CHANNEL DDR_TYPE (absolute) and its encoding ("" = reserved).
    ddr_type: F,
    ddr_names: &'static [&'static str],
    // Timing fields, offsets relative to the channel's timing block.
    cl: F,
    /// None: TC_PRE's tRP field "holds tRP (and tRCD)".
    rcd: Option<F>,
    rp: F,
    ras: F,
    rfc: F,
    /// SC_GS_CFG (relative) and its CMD_stretch field: 0 1N, 1 2N, 2 3N, 3 N:1.
    gs_cfg: u32,
    cmd: F,
    gear: Gear,
    /// SA_PERF_STATUS (5918h) DDR QCLK ratio and reference (0 = 133.33, 1 = 100 MHz).
    qclk: F,
    qref: F,
    /// BCLK_FREQ (5F60h, kHz) documented.
    bclk_reg: bool,
}

const SA_PERF: u32 = 0x5918;
const BCLK_FREQ: u32 = 0x5F60;
/// SA_PERF_STATUS RING UCLK ratio, reference 100 MHz (BCLK), same field on every layout.
const UCLK: F = f(SA_PERF, 31, 24);
const TWO_MC: &[(u32, u32)] = &[(0xE000, 0xD80C), (0xE800, 0xD810), (0x1_E000, 0x1_D80C), (0x1_E800, 0x1_D810)];

pub static SKL: Layout = Layout {
    chans: &[(0x4000, 0x500C), (0x4400, 0x5010)],
    dimm_l: f(0, 5, 0),
    dimm_s: f(0, 21, 16),
    ddr_type: f(0x5000, 1, 0),
    ddr_names: &["DDR4", "DDR3", "LPDDR3"],
    cl: f(0x070, 20, 16),
    rcd: None,
    rp: f(0x000, 5, 0),
    ras: f(0x000, 14, 8),
    rfc: f(0x23C, 25, 16),
    gs_cfg: 0x01C,
    cmd: f(0x01C, 3, 2),
    gear: Gear::None,
    qclk: f(SA_PERF, 6, 0),
    qref: f(SA_PERF, 7, 7),
    bclk_reg: false,
};

pub static ICL: Layout = Layout {
    chans: &[(0x4000, 0x500C), (0x4400, 0x5010)],
    dimm_l: f(0, 6, 0),
    dimm_s: f(0, 22, 16),
    ddr_type: f(0x5000, 2, 0),
    ddr_names: &["DDR4", "", "", "LPDDR4"],
    cl: f(0x070, 21, 16),
    rcd: None,
    rp: f(0x000, 5, 0),
    ras: f(0x000, 15, 9),
    rfc: f(0x23C, 25, 16),
    gs_cfg: 0x088,
    cmd: f(0x088, 4, 3),
    gear: Gear::G2,
    qclk: f(SA_PERF, 9, 2),
    qref: f(SA_PERF, 10, 10),
    bclk_reg: true,
};

pub static ADL: Layout = Layout {
    chans: TWO_MC,
    dimm_l: f(0, 6, 0),
    dimm_s: f(0, 22, 16),
    ddr_type: f(0xD800, 2, 0),
    ddr_names: &["DDR4", "DDR5", "LPDDR5", "LPDDR4"],
    cl: f(0x070, 22, 16),
    rcd: Some(f(0x000, 58, 51)),
    rp: f(0x000, 7, 0),
    ras: f(0x000, 50, 42),
    rfc: f(0x43C, 30, 18),
    gs_cfg: 0x088,
    cmd: f(0x088, 4, 3),
    gear: Gear::G2G4,
    qclk: f(SA_PERF, 9, 2),
    qref: f(SA_PERF, 10, 10),
    bclk_reg: true,
};

pub static ARL: Layout = Layout {
    chans: TWO_MC,
    dimm_l: f(0, 6, 0),
    dimm_s: f(0, 22, 16),
    ddr_type: f(0xD800, 2, 0),
    ddr_names: &["", "DDR5", "LPDDR5", "LPDDR4"],
    cl: f(0x070, 22, 16),
    rcd: Some(f(0x138, 29, 22)),
    rp: f(0x000, 7, 0),
    ras: f(0x000, 53, 45),
    rfc: f(0x4A0, 30, 18),
    gs_cfg: 0x088,
    cmd: f(0x088, 3, 3),
    gear: Gear::Mode,
    qclk: f(SA_PERF, 9, 2),
    qref: f(SA_PERF, 10, 10),
    bclk_reg: true,
};

/// Register layout for a CPUID family/model; None where no public datasheet
/// documents the IMC (Tiger/Rocket/Meteor/Lunar Lake, servers, Atom).
pub fn layout(family: u32, model: u32) -> Option<&'static Layout> {
    if family != 6 {
        return None;
    }
    Some(match model {
        // Skylake, Kaby/Coffee/Whiskey/Amber Lake, Comet Lake (client).
        0x4E | 0x5E | 0x8E | 0x9E | 0xA5 | 0xA6 => &SKL,
        0x7D | 0x7E => &ICL,
        // Alder Lake S/P, Raptor Lake S/P/HX.
        0x97 | 0x9A | 0xB7 | 0xBA | 0xBF => &ADL,
        // Arrow Lake S/HX, H, U.
        0xC6 | 0xC5 | 0xB5 => &ARL,
        _ => return None,
    })
}
pub fn channel_index(l:&Layout,base:u32)->u32 {l.chans.iter().position(|(b,_)|*b==base).unwrap() as u32}

#[derive(Debug, Clone, Default, PartialEq)]
pub struct Imc {
    pub channels: usize,
    pub ddr: Option<&'static str>,
    /// DRAM clock (half the MT/s rate).
    pub dram_mhz: Option<f64>,
    /// Memory controller clock (QCLK), Gear 1/2/4 layouts only.
    pub qclk_mhz: Option<f64>,
    pub ring_mhz: Option<f64>,
    /// Reference clock the ratios were scaled with.
    pub bclk_mhz: f64,
    pub measured_reference_mhz: Option<f64>,
    pub timings: Option<Timings>,
    pub channel_timings: Vec<(u32,Timings)>,
    pub channel_gears: Vec<(u32,u32)>,
}

/// Decode with `rd(offset)` reading a 32-bit MCHBAR register; `bclk` is the
/// fallback reference when BCLK_FREQ isn't available.
pub fn decode(l: &Layout, bclk: f64, rd: impl Fn(u32) -> Option<u32>) -> Option<Imc> {
    let get = |base: u32, x: F| rd(base + x.off).filter(|&v| v != u32::MAX).map(|v| x.get(v));
    let pop: Vec<u32> = l
        .chans
        .iter()
        .filter(|&&(_, mad)| get(mad, l.dimm_l).unwrap_or(0) + get(mad, l.dimm_s).unwrap_or(0) > 0)
        .map(|&(base, _)| base)
        .collect();
    let base = *pop.first()?;
    let ddr = get(0, l.ddr_type).and_then(|t| l.ddr_names.get(t as usize)).copied().filter(|n| !n.is_empty());
    let mut m = Imc { channels: pop.len(), ddr, bclk_mhz: bclk, ..Imc::default() };
    if l.bclk_reg {
        if let Some(k) = rd(BCLK_FREQ).map(|k| k as f64 / 1000.0).filter(|b| (50.0..200.0).contains(b)) { m.bclk_mhz = k;m.measured_reference_mhz=Some(k); }
    }
    let sa = rd(SA_PERF).filter(|&v| v != u32::MAX);
    m.ring_mhz = sa.map(|v| UCLK.get(v) as f64 * m.bclk_mhz).filter(|&r| r > 0.0);
    // LPDDR timings count WCK/sub-channel clocks; only DDR3/4/5 are decoded.
    if !ddr.is_some_and(|d| d.starts_with("DDR")) {
        return Some(m);
    }
    let gs = get(base, F { off: l.gs_cfg, lo: 0, bits: 32 }).unwrap_or(0);
    let bit = |n: u32| gs >> n & 1 == 1;
    let (gear, _no_div) = match l.gear {
        Gear::None => (1, false),
        Gear::G2 if bit(31) => (2, bit(30)),
        Gear::G2G4 if bit(31) => (2, bit(30)),
        Gear::G2G4 if bit(15) => (4, bit(16)),
        Gear::Mode if bit(31) => (4, bit(16)),
        Gear::Mode => (2, bit(30)),
        _ => (1, false),
    };
    let q =
        sa.map(|v| l.qclk.get(v) as f64 * if l.qref.get(v) == 1 { 100.0 } else { 400.0 / 3.0 } * m.bclk_mhz / 100.0);
    if let Some(q) = q.filter(|&q| q > 0.0) {
        m.dram_mhz = Some(q * gear as f64);
        m.qclk_mhz = (l.gear != Gear::None).then_some(q);
    }
    // "No GearN param divide": the fields hold DCLKs (2 tCK) instead of tCK.
    let timings = |base:u32| -> Option<Timings> {
        let gs=get(base,F{off:l.gs_cfg,lo:0,bits:32})?;
        let bit=|n:u32|gs>>n&1==1;
        let no_div=match l.gear {Gear::G2|Gear::G2G4 if bit(31)=>bit(30),Gear::G2G4 if bit(15)=>bit(16),Gear::Mode if bit(31)=>bit(16),Gear::Mode=>bit(30),_=>false};
        let k=if no_div{2}else{1};
        let rp = get(base, l.rp)? * k;
        let ras = get(base, l.ras)? * k;
        Some(Timings {
            cl: get(base, l.cl)? * k,
            trcd: l.rcd.map_or(Some(rp), |r| get(base, r).map(|v| v * k))?,
            trp: rp,
            tras: ras,
            // JEDEC: tRC = tRAS + tRP; the client IMC has no separate field.
            trc: ras + rp,
            cr: get(base, l.cmd).filter(|&c| c < 3).map(|c| c + 1),
            trfc: get(base, l.rfc).map(|r| r * k).filter(|&r| r > 0),
        })
    };
    m.channel_gears=pop.iter().filter_map(|&base|{
        let gs=get(base,F{off:l.gs_cfg,lo:0,bits:32})?;
        let gear=match l.gear {Gear::None=>1,Gear::G2|Gear::G2G4 if gs&(1<<31)!=0=>2,Gear::G2G4 if gs&(1<<15)!=0=>4,Gear::Mode if gs&(1<<31)!=0=>4,Gear::Mode=>2,_=>1};
        Some((base,gear))
    }).collect();
    m.channel_timings=pop.into_iter().filter_map(|base|timings(base).filter(super::sane).map(|t|(base,t))).collect();
    m.timings=m.channel_timings.first().map(|(_,t)|t.clone());
    Some(m)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;

    /// Register file from (offset, value) pairs; unset registers read 0.
    fn regs(v: &[(u32, u64)]) -> impl Fn(u32) -> Option<u32> + use<> {
        let mut m = HashMap::new();
        for &(o, x) in v {
            m.insert(o, x as u32);
            m.insert(o + 4, (x >> 32) as u32);
        }
        move |o| Some(m.get(&o).copied().unwrap_or(0))
    }

    #[test]
    fn generations() {
        assert!(std::ptr::eq(layout(6, 0x9E).unwrap(), &SKL));
        assert!(std::ptr::eq(layout(6, 0xB7).unwrap(), &ADL));
        assert!(std::ptr::eq(layout(6, 0xC6).unwrap(), &ARL));
        assert!(layout(6, 0x8C).is_none() && layout(0x19, 0x61).is_none());
    }

    #[test]
    fn field_split() {
        let x = f(0, 58, 51);
        assert_eq!((x.off, x.lo, x.bits), (4, 19, 8));
        // 13th Gen Vol. 2 TC_PRE default 104070180840C008h: tRP 8, tRAS 1Ch, tRCD 8.
        let r = regs(&[(0xE000, 0x1040_7018_0840_C008)]);
        assert_eq!(ADL.rp.get(r(0xE000).unwrap()), 8);
        assert_eq!(ADL.ras.get(r(0xE000 + ADL.ras.off).unwrap()), 0x1C);
        assert_eq!(ADL.rcd.unwrap().get(r(0xE004).unwrap()), 8);
    }

    #[test]
    fn skylake_ddr4() {
        // DDR4-3200 16-18-18-36, 2N, tRFC 560, one DIMM per channel, QCLK 12 x 133.33.
        let r = regs(&[
            (0x500C, 16),
            (0x5010, 16),
            (0x5000, 0),
            (0x4000, 18 | 36 << 8),
            (0x4070, 16 << 16),
            (0x423C, 560 << 16 | 0x1004),
            (0x401C, 1 << 2),
            (SA_PERF, 12 | 40 << 24),
        ]);
        let m = decode(&SKL, 100.0, r).unwrap();
        assert_eq!((m.channels, m.ddr, m.qclk_mhz), (2, Some("DDR4"), None));
        assert!((m.dram_mhz.unwrap() - 1600.0).abs() < 0.01);
        assert_eq!(m.ring_mhz, Some(4000.0));
        let t = m.timings.unwrap();
        assert_eq!((t.cl, t.trcd, t.trp, t.tras, t.trc, t.cr, t.trfc), (16, 18, 18, 36, 54, Some(2), Some(560)));
    }

    #[test]
    fn icelake_gear2() {
        // ICL TC_PRE default 18863808h: tRP 8, tRAS 1Ch.
        let r = regs(&[(0x500C, 8), (0x4000, 0x1886_3808), (0x4070, 0x0185_0000), (0x4088, 1 << 31)]);
        let m = decode(&ICL, 100.0, r).unwrap();
        let t = m.timings.unwrap();
        assert_eq!((t.trp, t.trcd, t.tras, t.cl, t.cr), (8, 8, 0x1C, 5, Some(1)));
    }

    #[test]
    fn raptor_ddr5_gear2() {
        // DDR5-6000 30-36-36-76, tRFC 884, two DIMMs (both channels of both MCs),
        // Gear 2: QCLK = 1500 MHz = ratio 15 x 100 MHz, BCLK_FREQ 100000 kHz.
        let pre = 36u64 | 76 << 42 | 36 << 51;
        let mut v = vec![(0xD800, 1u64), (SA_PERF, (15 << 2 | 1 << 10 | 47 << 24) as u64), (BCLK_FREQ, 100_000)];
        for &(base, mad) in TWO_MC {
            v.extend([
                (mad, 32u64),
                (base, pre),
                (base + 0x70, 30 << 16),
                (base + 0x43C, 884 << 18),
                (base + 0x88, 1 << 31),
            ]);
        }
        let m = decode(&ADL, 133.0, regs(&v)).unwrap();
        assert_eq!((m.channels, m.ddr, m.bclk_mhz), (4, Some("DDR5"), 100.0));
        assert_eq!((m.qclk_mhz, m.dram_mhz, m.ring_mhz), (Some(1500.0), Some(3000.0), Some(4700.0)));
        let t = m.timings.unwrap();
        assert_eq!((t.cl, t.trcd, t.trp, t.tras, t.trc, t.cr, t.trfc), (30, 36, 36, 76, 112, Some(1), Some(884)));
        // Gear 4 with no_gear4_param_divide: fields are half the tCK counts.
        let half = 18u64 | 38 << 42 | 18 << 51;
        let r = regs(&[(0xD800, 1), (0xD80C, 32), (0xE000, half), (0xE070, 15 << 16), (0xE088, 1 << 15 | 1 << 16)]);
        let t = decode(&ADL, 100.0, r).unwrap().timings.unwrap();
        assert_eq!((t.cl, t.trcd, t.tras), (30, 36, 76));
    }

    #[test]
    fn arrow_lake() {
        // DDR5-6400 32-39-39-102 Gear 2 (gear_mode 0), MC1 only populated.
        let r = regs(&[
            (0xD800, 1),
            (0x1_D80C, 48),
            (0x1_E000, 39 | 102 << 45),
            (0x1_E138, 39 << 22),
            (0x1_E070, 32 << 16),
            (0x1_E4A0, 0x02D0_1004),
            (0x1_E088, 1 << 3),
            (SA_PERF, 12 << 2),
        ]);
        let m = decode(&ARL, 100.0, r).unwrap();
        assert_eq!(m.channels, 1);
        assert!((m.dram_mhz.unwrap() - 6400.0 / 2.0).abs() < 0.01);
        let t = m.timings.unwrap();
        assert_eq!((t.cl, t.trcd, t.trp, t.tras, t.cr, t.trfc), (32, 39, 39, 102, Some(2), Some(0xB4)));
    }

    #[test]
    fn degrade() {
        // Nothing populated -> None; LPDDR5 -> no timings; all-ones (no access) -> None.
        assert!(decode(&ADL, 100.0, regs(&[])).is_none());
        let m = decode(&ADL, 100.0, regs(&[(0xD800, 2), (0xD80C, 16), (0xE000, 30)])).unwrap();
        assert_eq!((m.ddr, m.timings.is_none()), (Some("LPDDR5"), true));
        assert!(decode(&ADL, 100.0, |_| Some(u32::MAX)).is_none());
    }
    #[test]
    fn each_populated_channel_keeps_its_physical_number_gear_and_timings() {
        let r=regs(&[(0xD800,1),(0xD810,16),(0x1_D80C,16),
            (0xE800,36|76u64<<42|36u64<<51),(0xE870,30<<16),(0xE888,1<<31),
            (0x1_E000,20|40u64<<42|20u64<<51),(0x1_E070,16<<16),(0x1_E088,1<<15|1<<16)]);
        let m=decode(&ADL,100.0,r).unwrap();assert_eq!(m.channel_timings.len(),2);
        assert_eq!(channel_index(&ADL,m.channel_timings[0].0),1);assert_eq!(channel_index(&ADL,m.channel_timings[1].0),2);
        assert_eq!((m.channel_timings[0].1.cl,m.channel_timings[1].1.cl),(30,32));
        assert_eq!(m.channel_gears,vec![(0xE800,2),(0x1_E000,4)]);
        assert_eq!(decode(&ADL,100.0,|_|None),None);
    }
}
