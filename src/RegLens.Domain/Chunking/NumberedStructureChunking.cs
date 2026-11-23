using System.Globalization;
using RegLens.Domain.Corpus;

namespace RegLens.Domain.Chunking;

/// <summary>
/// Cuts a document into one passage per numbered paragraph or rule, with its heading trail (spec 0001, R1.13–R1.14).
/// Numbers only count when they follow the previous one closely, and chapters only when they come in order,
/// so table-of-contents leftovers and stray numbered lines (signature lists, amounts) stay plain text.
/// </summary>
public sealed class NumberedStructureChunking(StructureMarkers markers) : IChunkingStrategy
{
    public const string PreambleRef = "Preamble";

    /// <summary>Text right after a heading this short is the heading's own continuation or title, not a passage.</summary>
    public const int MaxIntroLines = 2;

    public const int MaxIntroTokens = 30;

    public DocumentStructure SupportedStructure => markers.Structure;

    public IReadOnlyList<Passage> Segment(IReadOnlyList<PageLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var state = new SegmentState(markers);
        foreach (var line in lines)
        {
            state.Accept(line);
        }

        return state.Finish();
    }

    private sealed class SegmentState(StructureMarkers markers)
    {
        private readonly List<Passage> _passages = [];
        private readonly string?[] _trail = new string?[markers.Headings.Count];
        private int _lastChapter;
        private int _lastNumber;
        private string? _container;
        private string? _containerTitle;
        private string? _containerPart;
        private string? _partTitle;
        private Action<string>? _absorbIntro;
        private string _paraRef = PreambleRef;
        private string _heading = "";
        private List<PageLine> _lines = [];

        public void Accept(PageLine line)
        {
            if (TryContainer(line.Text) || TryContainerPart(line.Text) || TryHeading(line.Text) || TryUnit(line))
            {
                return;
            }

            _lines.Add(line);
        }

        public List<Passage> Finish()
        {
            Flush();
            return _passages;
        }

        private bool TryContainer(string text)
        {
            var label = markers.Container(text);
            if (label is null)
            {
                return false;
            }

            AbsorbIntro();
            _container = label;
            _containerTitle = null;
            _containerPart = null;
            _partTitle = null;
            _lastNumber = 0;
            Array.Clear(_trail);
            Start(label, ContainerHeading(), title => _containerTitle = title);
            return true;
        }

        private bool TryContainerPart(string text)
        {
            var label = _container is null ? null : markers.ContainerPart(text);
            if (label is null)
            {
                return false;
            }

            AbsorbIntro();
            _containerPart = label;
            _partTitle = null;
            _lastNumber = 0;
            Start(ContainerRef(), ContainerHeading(), title => _partTitle = title);
            return true;
        }

        private bool TryHeading(string text)
        {
            if (_container is not null)
            {
                return false;
            }

            for (var level = 0; level < markers.Headings.Count; level++)
            {
                var label = markers.Headings[level](text);
                if (label is null || (level == 0 && !IsNextChapter(text)))
                {
                    continue;
                }

                AbsorbIntro();
                _trail[level] = label;
                Array.Clear(_trail, level + 1, _trail.Length - level - 1);
                var trailLevel = level;
                Start(label, Trail(), title => _trail[trailLevel] = $"{_trail[trailLevel]} {title}");
                return true;
            }

            return false;
        }

        private bool IsNextChapter(string text)
        {
            if (markers.ChapterNumber(text) is not { } chapter
                || chapter <= _lastChapter
                || chapter > _lastChapter + StructureMarkers.MaxNumberGap)
            {
                return false;
            }

            _lastChapter = chapter;
            return true;
        }

        private bool TryUnit(PageLine line)
        {
            var number = StructureMarkers.UnitNumber(line.Text);
            if (number is not { } n || n <= _lastNumber || n > _lastNumber + StructureMarkers.MaxNumberGap)
            {
                return false;
            }

            AbsorbIntro();
            _lastNumber = n;
            var unitRef = _container is null
                ? string.Create(CultureInfo.InvariantCulture, $"{markers.UnitName} {n}")
                : string.Create(CultureInfo.InvariantCulture, $"{ContainerRef()}, {markers.ContainerItemName} {n}");
            Start(unitRef, _container is null ? Trail() : ContainerHeading(), absorbIntro: null);
            _lines.Add(line);
            return true;
        }

        // A short run of lines right after a heading is its wrapped title or caption: fold it into the heading.
        private void AbsorbIntro()
        {
            var absorb = _absorbIntro;
            _absorbIntro = null;
            var text = string.Join(' ', _lines.Select(l => l.Text));
            if (absorb is null || _lines.Count is 0 or > MaxIntroLines || TokenEstimate.Of(text) >= MaxIntroTokens)
            {
                return;
            }

            absorb(text);
            _lines.Clear();
        }

        private void Start(string paraRef, string heading, Action<string>? absorbIntro)
        {
            Flush();
            _paraRef = paraRef;
            _heading = heading;
            _absorbIntro = absorbIntro;
        }

        private void Flush()
        {
            if (_lines.Count > 0)
            {
                _passages.Add(new Passage(_paraRef, _heading, _lines));
            }

            _lines = [];
        }

        private string ContainerRef() => _containerPart is null ? _container! : $"{_container}, {_containerPart}";

        private string ContainerHeading()
        {
            var container = _containerTitle is null ? _container! : $"{_container} — {_containerTitle}";
            if (_containerPart is null)
            {
                return container;
            }

            return _partTitle is null ? $"{container} › {_containerPart}" : $"{container} › {_containerPart} — {_partTitle}";
        }

        private string Trail() => string.Join(" › ", _trail.Where(t => t is not null));
    }
}
