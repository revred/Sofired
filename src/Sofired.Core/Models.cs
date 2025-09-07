namespace Sofired.Core;
public record DailyBar(System.DateTime Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
public enum VolRegime { Low, Normal, High }