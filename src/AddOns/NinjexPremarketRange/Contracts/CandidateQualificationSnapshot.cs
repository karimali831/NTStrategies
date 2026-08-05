namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class CandidateQualificationSnapshot
    {
        public static readonly CandidateQualificationSnapshot Passed =
            new CandidateQualificationSnapshot(
                true,
                true,
                true,
                true);

        public bool DirectionPassed { get; }
        public bool BodyPassed { get; }
        public bool CloseLocationPassed { get; }
        public bool RelativeBodyPassed { get; }

        public CandidateQualificationSnapshot(
            bool directionPassed,
            bool bodyPassed,
            bool closeLocationPassed,
            bool relativeBodyPassed)
        {
            DirectionPassed = directionPassed;
            BodyPassed = bodyPassed;
            CloseLocationPassed = closeLocationPassed;
            RelativeBodyPassed = relativeBodyPassed;
        }
    }
}