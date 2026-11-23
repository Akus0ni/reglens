using RegLens.Domain.Corpus;

namespace RegLens.Domain.Chunking;

/// <summary>
/// Cuts a document into one passage per numbered paragraph or rule, with its heading trail (spec 0001, R1.13–R1.14).
/// Numbers only count when they follow the previous one closely, and chapters only when they come in order,
/// so table-of-contents leftovers and stray numbered lines (signature lists, amounts) stay plain text.
/// Spec 0005 (H1–H5): wrapped heading lines join their heading, sections before the first chapter are table of
/// contents, and a paragraph inserted by amendment ("105A.") follows its base paragraph.
/// </summary>
public sealed class NumberedStructureChunking(StructureMarkers markers) : IChunkingStrategy
{
    public const string PreambleRef = "Preamble";

    /// <summary>Text right after a heading this short is the heading's own continuation or title, not a passage.</summary>
    public const int MaxIntroLines = 2;

    public const int MaxIntroTokens = 30;

    // A heading that ends with one of these words is cut mid-phrase, so the next line continues it.
    private static readonly HashSet<string> OpenEndings = new(StringComparer.OrdinalIgnoreCase)
    {
        "of", "in", "the", "and", "for", "to", "on",
    };

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
        private char? _lastSuffix;
        private int? _joinLevel;
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
            if (TryJoinHeadingLine(line.Text)
                || TryContainer(line.Text)
                || TryContainerPart(line.Text)
                || TryHeading(line.Text)
                || TryUnit(line))
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
            _lastSuffix = null;
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
            _lastSuffix = null;
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
                if (label is null || (level == 0 ? !IsNextChapter(text) : _lastChapter == 0))
                {
                    // Sections before the first chapter are the table of contents (H3).
                    continue;
                }

                AbsorbIntro();
                _trail[level] = label;
                Array.Clear(_trail, level + 1, _trail.Length - level - 1);
                var trailLevel = level;
                Start(label, Trail(), title => _trail[trailLevel] = $"{_trail[trailLevel]} {title}");
                _joinLevel = level;
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

        // A heading wrapped over several lines (H1, H2): the next line joins while the heading is visibly unfinished.
        private bool TryJoinHeadingLine(string text)
        {
            if (_joinLevel is not { } level)
            {
                return false;
            }

            var heading = _trail[level]!;
            if (markers.StartsStructure(text) || !IsUnfinished(heading, text))
            {
                _joinLevel = null;
                return false;
            }

            _trail[level] = $"{heading} {text}";
            _paraRef = _trail[level]!;
            _heading = Trail();
            return true;
        }

        private static bool IsUnfinished(string heading, string next)
        {
            var lastWord = heading[(heading.LastIndexOf(' ') + 1)..];
            return heading.EndsWith('/')
                || OpenEndings.Contains(lastWord)
                || heading.Count(c => c == '(') > heading.Count(c => c == ')')
                || next.StartsWith('/')
                || char.IsLower(next[0]);
        }

        private bool TryUnit(PageLine line)
        {
            if (StructureMarkers.Unit(line.Text) is not { } unit || !IsNextUnit(unit))
            {
                return false;
            }

            AbsorbIntro();
            _lastNumber = unit.Number;
            _lastSuffix = unit.Suffix;
            var unitRef = _container is null
                ? $"{markers.UnitName} {unit}"
                : $"{ContainerRef()}, {markers.ContainerItemName} {unit}";
            Start(unitRef, _container is null ? Trail() : ContainerHeading(), absorbIntro: null);
            _lines.Add(line);
            return true;
        }

        // "105A." only right after "105." (or "105" + an earlier letter); a plain number must move forward, but not far.
        private bool IsNextUnit(UnitStart unit) => unit.Suffix is { } suffix
            ? unit.Number == _lastNumber && (_lastSuffix is null || suffix > _lastSuffix)
            : unit.Number > _lastNumber && unit.Number <= _lastNumber + StructureMarkers.MaxNumberGap;

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
            _joinLevel = null;
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
