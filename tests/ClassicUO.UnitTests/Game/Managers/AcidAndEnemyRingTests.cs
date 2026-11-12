using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers
{
    public class AcidFilterTests
    {
        [Theory]
        [InlineData("0x122A 83", 0x122A, 83)]
        [InlineData("0x122a, 0x53", 0x122A, 0x53)]
        [InlineData("4650\t83   # comment", 4650, 83)]
        [InlineData("  0x122B 0x95 ; dried ooze", 0x122B, 0x95)]
        public void ParsesGraphicAndHue(string line, int graphic, int hue)
        {
            Assert.True(AcidFilter.TryParseLine(line, out ushort g, out ushort h));
            Assert.Equal(graphic, g);
            Assert.Equal(hue, h);
        }

        [Theory]
        [InlineData("")]
        [InlineData("# only a comment")]
        [InlineData("0x122A")]          // hue missing
        [InlineData("blood 83")]
        [InlineData("0x122A 70000")]    // hue out of range
        public void IgnoresBadLines(string line)
        {
            Assert.False(AcidFilter.TryParseLine(line, out _, out _));
        }

        [Fact]
        public void DefaultFile_MatchesAcidButNotBlood()
        {
            AcidFilter.LoadFromText(AcidFilter.DefaultFileText());

            Assert.Equal(AcidFilter.Defaults.Length, AcidFilter.Count);
            Assert.True(AcidFilter.IsAcid(0x122A, 0x3F));   // TrueUO Pool of Acid
            Assert.True(AcidFilter.IsAcid(0x122A, 83));     // UO Dreams acid
            Assert.True(AcidFilter.IsAcid(0x122A, 1932));   // Explosive Goo
            Assert.True(AcidFilter.IsAcid(0x122A, 0x95));   // Interred Grizzle ooze
            Assert.True(AcidFilter.IsAcid(0x122B, 0x95));   // dried ooze
            Assert.False(AcidFilter.IsAcid(0x122A, 0));     // plain blood
            Assert.False(AcidFilter.IsAcid(0x122B, 0x3F));  // other graphic, acid hue
        }

        [Fact]
        public void EmptyFile_MatchesNothing()
        {
            AcidFilter.LoadFromText("# nothing\n");
            Assert.False(AcidFilter.IsAcid(0x122A, 83));
            AcidFilter.LoadDefaults();
            Assert.True(AcidFilter.IsAcid(0x122A, 83));
        }
    }

    public class EnemyRingTests
    {
        [Fact]
        public void Combatant_IsStickyAcrossZero()
        {
            EnemyRing.Clear();
            EnemyRing.OnCombatant(0x1234);
            EnemyRing.OnCombatant(0);          // server: combat dropped
            Assert.Equal(0x1234u, EnemyRing.Serial);
            EnemyRing.OnCombatant(0x5678);     // new enemy
            Assert.Equal(0x5678u, EnemyRing.Serial);
            EnemyRing.Clear();
            Assert.Equal(0u, EnemyRing.Serial);
        }

        [Fact]
        public void Uv_NoRotation_MapsFloorCornersToTextureCorners()
        {
            var tl = EnemyRing.Uv(-1, -1, 1, 0);
            var br = EnemyRing.Uv(1, 1, 1, 0);
            var c = EnemyRing.Uv(0, 0, 1, 0);

            Assert.Equal(0f, tl.X, 4); Assert.Equal(0f, tl.Y, 4);
            Assert.Equal(1f, br.X, 4); Assert.Equal(1f, br.Y, 4);
            Assert.Equal(0.5f, c.X, 4); Assert.Equal(0.5f, c.Y, 4);
        }

        [Fact]
        public void Uv_QuarterTurn_KeepsCentreAndRadius()
        {
            // 90 degrees: a point on the right edge of the floor samples the top or bottom of the art.
            var p = EnemyRing.Uv(1, 0, 0, 1);
            Assert.Equal(0.5f, p.X, 4);
            Assert.Equal(0.5f, System.Math.Abs(p.Y - 0.5f), 4);
        }

        [Fact]
        public void Art_HasTransparentBorder_AndVisibleRings()
        {
            const int size = 160;
            var art = EnemyRing.GenerateArt(size);

            for (int i = 0; i < size; i++)
            {
                // all four edges must be fully transparent (clamped sampling repeats them)
                Assert.Equal(0, art[i].A);
                Assert.Equal(0, art[i + (size - 1) * size].A);
                Assert.Equal(0, art[i * size].A);
                Assert.Equal(0, art[size - 1 + i * size].A);
            }

            // outer ring at 0.84 of the half-size, straight down from the centre
            int y = (int) (size / 2 + 0.84f * size / 2);
            Assert.True(art[size / 2 + y * size].A > 200);

            // centre is only a faint fill
            Assert.True(art[size / 2 + size / 2 * size].A < 40);
        }

        [Fact]
        public void Art_HasArrowsOnDiagonals()
        {
            // inside an arrow near its base, on the 45 degree diagonal
            float u = 0.95f;
            float x = u * 0.70710677f, y = u * 0.70710677f;
            Assert.Equal(1f, EnemyRing.ArtAlpha(x, y));

            // same radius, straight to the right: no arrow (outside the outer ring)
            Assert.Equal(0f, EnemyRing.ArtAlpha(0.95f, 0f));
        }
    }
}
