import { formatBps, formatDelta, formatMillions, formatMoney, formatPercent } from './amount';

describe('formatters', () => {
  it('abbreviates amounts with their currency', () => {
    expect(formatMoney(1_234_000_000)).toBe('1.2 bn EUR');
    expect(formatMoney(-345_640_000, 'USD')).toBe('-345.6 m USD');
    expect(formatMoney(12_300)).toBe('12.3 k EUR');
    expect(formatMoney(950)).toBe('950 EUR');
    expect(formatMoney(null)).toBe('-');
  });

  it('chooses the magnitude after rounding', () => {
    expect(formatMoney(999_960)).toBe('1.0 m EUR');
    expect(formatMoney(-999_960)).toBe('-1.0 m EUR');
    expect(formatMoney(999_949)).toBe('999.9 k EUR');
    expect(formatMoney(999.6)).toBe('1.0 k EUR');
    expect(formatMoney(999.4)).toBe('999 EUR');
    expect(formatMoney(999_960_000)).toBe('1.0 bn EUR');
    expect(formatMoney(1_000_000)).toBe('1.0 m EUR');
  });

  it('formats millions, percentages and basis points', () => {
    expect(formatMillions(1_234_500_000)).toBe('1,234.5 m');
    expect(formatPercent(0.853)).toBe('85.3%');
    expect(formatBps(12.4)).toBe('12 bps');
  });

  it('signs a delta and gives its direction', () => {
    expect(formatDelta(0.05, formatPercent)).toEqual({ text: '+5.0%', direction: 'up' });
    expect(formatDelta(-3, String)).toEqual({ text: '-3', direction: 'down' });
    expect(formatDelta(0, String)).toEqual({ text: '0', direction: 'flat' });
    expect(formatDelta(null, String)).toBeNull();
  });
});
