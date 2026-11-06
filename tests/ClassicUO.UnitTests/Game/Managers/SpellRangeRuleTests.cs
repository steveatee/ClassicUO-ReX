using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class SpellRangeRuleTests
    {
        [Theory]
        [InlineData(9, 0, true)]
        [InlineData(0, -9, true)]
        [InlineData(6, 6, true)]   // 72 <= 81
        [InlineData(7, 5, true)]   // 74
        [InlineData(8, 5, false)]  // 89
        [InlineData(9, 1, false)]  // 82
        [InlineData(10, 0, false)]
        public void Circle_UsesSquaredDistance(int dx, int dy, bool expected)
        {
            Assert.Equal(expected, new SpellRangeRule(9, true).Contains(dx, dy));
        }

        [Theory]
        [InlineData(0, -9, true)]
        [InlineData(4, -9, true)]   // 97
        [InlineData(-4, -9, true)]
        [InlineData(5, -9, false)]  // 106
        [InlineData(0, -10, false)] // square limit
        [InlineData(7, 7, true)]    // 98
        [InlineData(8, 6, true)]    // 100
        [InlineData(8, 7, false)]   // 113
        public void SquareWithCutCorners(int dx, int dy, bool expected)
        {
            Assert.Equal(expected, new SpellRangeRule(9, 10).Contains(dx, dy));
        }

        [Theory]
        [InlineData(9, 9, true)]
        [InlineData(-9, 4, true)]
        [InlineData(10, 0, false)]
        public void Square_IsStockInRange(int dx, int dy, bool expected)
        {
            Assert.Equal(expected, new SpellRangeRule(9, false).Contains(dx, dy));
        }
    }
}
