using Microsoft.EntityFrameworkCore;
using OfficeAI.Application.Abstractions;
using OfficeAI.Domain.Common;
using OfficeAI.Domain.Delivery;
using OfficeAI.Domain.Knowledge;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Delivery;

/// <summary>One piece of information the intake needs. Blocking checks must be met before the lead can be qualified.</summary>
public record LeadCheckDto(string Code, bool Met, bool Blocking);
public record LeadQuestionDto(Guid Id, string Topic, string Question, string? Answer, DateTimeOffset AskedAt, DateTimeOffset? AnsweredAt, uint Version);
public record LeadDto(Guid Id, string Code, string Title, string CustomerName, string? ContactName, string? ContactEmail, string? ContactPhone, string? Source,
    string Need, string? BusinessGoal, string? TargetUsers, string? Platforms, string? Constraints, decimal? Budget, DateOnly? DesiredDueDate,
    LeadStatus Status, Guid? ProjectId, string? ProjectCode, DateTimeOffset? ConvertedAt, string? LostReason, List<LeadCheckDto> Checks, bool Ready,
    List<LeadQuestionDto> Questions, DateTimeOffset CreatedAt, uint Version);
public record SaveLeadRequest(string Title, string CustomerName, string Need, string? ContactName = null, string? ContactEmail = null, string? ContactPhone = null,
    string? Source = null, string? BusinessGoal = null, string? TargetUsers = null, string? Platforms = null, string? Constraints = null,
    decimal? Budget = null, DateOnly? DesiredDueDate = null, uint? Version = null);
public record AskLeadQuestionRequest(string Question, string? Topic = null);
public record SuggestLeadQuestionsRequest(string? Language = null);
public record AnswerLeadQuestionRequest(string Answer, uint? Version = null);
public record LeadDecisionRequest(string? Reason = null, uint? Version = null);
public record ConvertLeadRequest(string Code, string? Name = null, Guid? OwnerEmployeeId = null, uint? Version = null);

/// <summary>
/// Customer intake (Master Prompt §7 stage 1): record the request, detect what is missing, ask the customer and record the
/// answers, and only then turn it into a project with a written brief. Nothing is sent to the customer from here; people relay it.
/// </summary>
public class LeadService(IAppDbContext db, TimeProvider clock)
{
    public const int MaxQuestionsPerLead = 50;
    /// <summary>Shorter than this the request cannot be understood without asking.</summary>
    public const int MinNeedLength = 40;

    /// <summary>The missing-information checks, in the order they should be asked about.</summary>
    public static List<LeadCheckDto> Assess(Lead l)
    {
        static bool Has(string? s) => !string.IsNullOrWhiteSpace(s);
        var open = l.Questions.Count(q => q.AnsweredAt is null);
        return
        [
            new("contact", Has(l.ContactEmail) || Has(l.ContactPhone), true),
            new("needDetail", l.Need.Trim().Length >= MinNeedLength, true),
            new("businessGoal", Has(l.BusinessGoal), true),
            new("targetUsers", Has(l.TargetUsers), true),
            new("budget", l.Budget is not null, true),
            new("desiredDueDate", l.DesiredDueDate is not null, true),
            new("platforms", Has(l.Platforms), false),
            new("constraints", Has(l.Constraints), false),
            new("questionsAnswered", open == 0, true),
        ];
    }

    private static readonly Dictionary<string, (string Th, string En)> Templates = new()
    {
        ["contact"] = ("ขออีเมลหรือเบอร์โทรของผู้ติดต่อหลัก เพื่อใช้ส่งเอกสารและนัดคุยครับ", "What email or phone number should we use for the main contact?"),
        ["needDetail"] = ("ช่วยเล่ารายละเอียดงานเพิ่มเติม: ระบบต้องทำอะไรได้บ้าง ผู้ใช้ทำงานอะไรผ่านระบบ และตอนนี้ทำงานนี้อยู่อย่างไร", "Could you describe the work in more detail: what the system must do, what users will do with it, and how this is done today?"),
        ["businessGoal"] = ("เป้าหมายทางธุรกิจของงานนี้คืออะไร และจะวัดว่าสำเร็จอย่างไร", "What business goal should this achieve, and how will you measure success?"),
        ["targetUsers"] = ("ใครคือผู้ใช้หลัก (เช่น ลูกค้า พนักงาน ผู้ดูแลระบบ) และมีประมาณกี่คน", "Who are the main users (customers, staff, administrators…) and roughly how many?"),
        ["budget"] = ("มีงบประมาณหรือช่วงงบที่ตั้งไว้สำหรับงานนี้ไหมครับ", "Do you have a budget or budget range in mind?"),
        ["desiredDueDate"] = ("ต้องการให้ใช้งานได้ภายในวันที่เท่าไร และมีเหตุผลเรื่องวันนั้นไหม", "By when do you need it in use, and what drives that date?"),
        ["platforms"] = ("ต้องการใช้งานบนเว็บ มือถือ หรือเดสก์ท็อป และต้องเชื่อมกับระบบอื่นไหม", "Should it run on web, mobile or desktop, and does it need to connect to other systems?"),
        ["constraints"] = ("มีข้อจำกัดอะไรบ้าง เช่น เทคโนโลยีที่ต้องใช้ กฎหมาย/ข้อมูลส่วนบุคคล หรือที่ติดตั้งระบบ", "Are there constraints, such as required technology, legal or privacy rules, or where it must be hosted?"),
    };

    public async Task<List<LeadDto>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Leads.AsNoTracking().Include(l => l.Questions).OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task<LeadDto> GetAsync(Guid id, CancellationToken ct)
    {
        var l = await db.Leads.AsNoTracking().Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lead", id);
        return (await ToDtosAsync([l], ct))[0];
    }

    private async Task<List<LeadDto>> ToDtosAsync(List<Lead> rows, CancellationToken ct)
    {
        var projectIds = rows.Select(l => l.ProjectId).OfType<Guid>().ToList();
        var codes = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        return rows.Select(l =>
        {
            var checks = Assess(l);
            return new LeadDto(l.Id, l.Code, l.Title, l.CustomerName, l.ContactName, l.ContactEmail, l.ContactPhone, l.Source, l.Need, l.BusinessGoal,
                l.TargetUsers, l.Platforms, l.Constraints, l.Budget, l.DesiredDueDate, l.Status, l.ProjectId,
                l.ProjectId is { } p ? codes.GetValueOrDefault(p) : null, l.ConvertedAt, l.LostReason, checks, checks.All(c => c.Met || !c.Blocking),
                l.Questions.OrderBy(q => q.Sequence).Select(q => new LeadQuestionDto(q.Id, q.Topic, q.Question, q.Answer, q.CreatedAt, q.AnsweredAt, q.Version)).ToList(),
                l.CreatedAt, l.Version);
        }).ToList();
    }

    public async Task<LeadDto> CreateAsync(SaveLeadRequest req, CancellationToken ct)
    {
        var l = new Lead { Code = "", Title = "", CustomerName = "", Need = "" };
        Apply(l, req);
        // Codes are never reused, so count every lead ever made.
        l.Code = $"LEAD-{await db.Leads.IgnoreQueryFilters().CountAsync(ct) + 1:000}";
        db.Leads.Add(l);
        await db.SaveChangesAsync(ct);
        return await GetAsync(l.Id, ct);
    }

    public async Task<LeadDto> UpdateAsync(Guid id, SaveLeadRequest req, CancellationToken ct)
    {
        var l = await LoadAsync(id, req.Version, ct);
        if (l.Status is LeadStatus.Converted or LeadStatus.Lost) throw new DomainException("lead.closed", $"{l.Code} is {l.Status}; it can no longer be edited.");
        Apply(l, req);
        // Editing a qualified lead may remove something essential; it must be qualified again.
        if (l.Status == LeadStatus.Qualified && !Assess(l).All(c => c.Met || !c.Blocking)) l.Status = LeadStatus.Clarifying;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static void Apply(Lead l, SaveLeadRequest r)
    {
        Validate.Required(r.Title, "title");
        Validate.Required(r.CustomerName, "customerName");
        Validate.Required(r.Need, "need");
        if (r.Title.Trim().Length > 200) throw new DomainException("validation", "title must be at most 200 characters.");
        if (r.Budget is < 0) throw new DomainException("validation", "budget cannot be negative.");
        if (r.ContactEmail is { Length: > 0 } email && !email.Contains('@')) throw new DomainException("validation", "contactEmail is not an email address.");
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        l.Title = r.Title.Trim();
        l.CustomerName = r.CustomerName.Trim();
        l.Need = r.Need.Trim();
        l.ContactName = Clean(r.ContactName);
        l.ContactEmail = Clean(r.ContactEmail);
        l.ContactPhone = Clean(r.ContactPhone);
        l.Source = Clean(r.Source);
        l.BusinessGoal = Clean(r.BusinessGoal);
        l.TargetUsers = Clean(r.TargetUsers);
        l.Platforms = Clean(r.Platforms);
        l.Constraints = Clean(r.Constraints);
        l.Budget = r.Budget;
        l.DesiredDueDate = r.DesiredDueDate;
    }

    /// <summary>Records a question to relay to the customer. Allowed after conversion too, for follow-up clarifications.</summary>
    public async Task<LeadDto> AskAsync(Guid id, AskLeadQuestionRequest req, CancellationToken ct)
    {
        Validate.Required(req.Question, "question");
        var l = await LoadAsync(id, null, ct);
        if (l.Status == LeadStatus.Lost) throw new DomainException("lead.closed", $"{l.Code} is Lost.");
        var topic = string.IsNullOrWhiteSpace(req.Topic) ? "other" : req.Topic.Trim();
        if (topic != "other" && !Templates.ContainsKey(topic)) throw new DomainException("validation", "topic is invalid.");
        AddQuestions(l, [(topic, req.Question.Trim())]);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>One question per missing item that has no unanswered question yet, in the customer's language.</summary>
    public async Task<LeadDto> SuggestQuestionsAsync(Guid id, SuggestLeadQuestionsRequest req, CancellationToken ct)
    {
        var l = await LoadAsync(id, null, ct);
        if (l.Status is LeadStatus.Converted or LeadStatus.Lost) throw new DomainException("lead.closed", $"{l.Code} is {l.Status}.");
        var english = string.Equals(req.Language, "en", StringComparison.OrdinalIgnoreCase);
        var asked = l.Questions.Where(q => q.AnsweredAt is null).Select(q => q.Topic).ToHashSet();
        var add = Assess(l).Where(c => !c.Met && Templates.ContainsKey(c.Code) && !asked.Contains(c.Code))
            .Select(c => (c.Code, english ? Templates[c.Code].En : Templates[c.Code].Th)).ToList();
        if (add.Count == 0) throw new DomainException("lead.nothing_missing", "Nothing is missing that has not already been asked.");
        AddQuestions(l, add);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private void AddQuestions(Lead l, List<(string Topic, string Text)> items)
    {
        if (l.Questions.Count + items.Count > MaxQuestionsPerLead)
            throw new DomainException("lead.limit", $"A lead can hold at most {MaxQuestionsPerLead} questions.");
        var next = l.Questions.Count == 0 ? 1 : l.Questions.Max(q => q.Sequence) + 1;
        foreach (var (topic, text) in items)
        {
            if (text.Length > 2000) throw new DomainException("validation", "question must be at most 2000 characters.");
            db.LeadQuestions.Add(new LeadQuestion { LeadId = l.Id, Sequence = next++, Topic = topic, Question = text });
        }
        if (l.Status == LeadStatus.New) l.Status = LeadStatus.Clarifying;
        // An open question means the lead is not fully known any more.
        if (l.Status == LeadStatus.Qualified) l.Status = LeadStatus.Clarifying;
    }

    /// <summary>Records the customer's answer as relayed by a person. The lead fields are updated separately.</summary>
    public async Task<LeadDto> AnswerAsync(Guid id, Guid questionId, AnswerLeadQuestionRequest req, CancellationToken ct)
    {
        Validate.Required(req.Answer, "answer");
        var q = await db.LeadQuestions.FirstOrDefaultAsync(x => x.Id == questionId && x.LeadId == id, ct) ?? throw new NotFoundException("LeadQuestion", questionId);
        if (req.Version is { } v) db.ExpectVersion(q, v);
        if (req.Answer.Trim().Length > 8000) throw new DomainException("validation", "answer must be at most 8000 characters.");
        q.Answer = req.Answer.Trim();
        q.AnsweredAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Everything essential is known and every question is answered.</summary>
    public async Task<LeadDto> QualifyAsync(Guid id, LeadDecisionRequest req, CancellationToken ct)
    {
        var l = await LoadAsync(id, req.Version, ct);
        if (l.Status is not (LeadStatus.New or LeadStatus.Clarifying)) throw new DomainException("lead.status", $"{l.Code} is {l.Status}.");
        var missing = Assess(l).Where(c => c.Blocking && !c.Met).Select(c => c.Code).ToList();
        if (missing.Count > 0) throw new DomainException("lead.incomplete", $"Still missing: {string.Join(", ", missing)}.");
        l.Status = LeadStatus.Qualified;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<LeadDto> MarkLostAsync(Guid id, LeadDecisionRequest req, CancellationToken ct)
    {
        Validate.Required(req.Reason, "reason");
        var l = await LoadAsync(id, req.Version, ct);
        if (l.Status is LeadStatus.Converted or LeadStatus.Lost) throw new DomainException("lead.closed", $"{l.Code} is already {l.Status}.");
        l.Status = LeadStatus.Lost;
        l.LostReason = req.Reason!.Trim();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Creates the Draft project and its brief from a qualified lead. The brief is the stage 1 deliverable.</summary>
    public async Task<LeadDto> ConvertAsync(Guid id, ConvertLeadRequest req, CancellationToken ct)
    {
        Validate.Required(req.Code, "code");
        var l = await LoadAsync(id, req.Version, ct);
        if (l.Status != LeadStatus.Qualified) throw new DomainException("lead.not_qualified", $"{l.Code} must be qualified before it becomes a project.");
        var code = req.Code.Trim();
        if (code.Length > 50) throw new DomainException("validation", "code must be at most 50 characters.");
        if (await db.Projects.IgnoreQueryFilters().AnyAsync(p => p.Code == code, ct)) throw new DomainException("lead.code_taken", $"Project code {code} is already used.");
        if (req.OwnerEmployeeId is { } owner && !await db.Employees.AnyAsync(e => e.Id == owner, ct)) throw new NotFoundException("Employee", owner);

        var brief = Brief(l);
        var project = new Project
        {
            Code = code, Name = string.IsNullOrWhiteSpace(req.Name) ? l.Title : req.Name.Trim(), Status = ProjectStatus.Draft,
            Description = Summary(l), EstimatedBudget = l.Budget, DueDate = l.DesiredDueDate, OwnerEmployeeId = req.OwnerEmployeeId,
        };
        db.Projects.Add(project);
        db.Documents.Add(new ProjectDocument { ProjectId = project.Id, Kind = DocumentKind.Other, Title = $"Project brief ({l.Code})", Revision = 1, Content = brief });
        l.Status = LeadStatus.Converted;
        l.ProjectId = project.Id;
        l.ConvertedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static string Summary(Lead l) => string.Join("\n\n", new[]
    {
        l.Need,
        l.BusinessGoal is { } g ? $"Goal: {g}" : null,
        l.TargetUsers is { } u ? $"Users: {u}" : null,
    }.OfType<string>());

    /// <summary>The customer's request and every clarification, as one Markdown document.</summary>
    internal static string Brief(Lead l)
    {
        static string Line(string label, string? value) => value is null ? "" : $"- **{label}:** {value}\n";
        var qa = l.Questions.Where(q => q.Answer is not null).OrderBy(q => q.Sequence).Select(q => $"**Q:** {q.Question}\n\n**A:** {q.Answer}\n").ToList();
        return $"# {l.Title}\n\n" +
            $"## Customer\n\n{Line("Customer", l.CustomerName)}{Line("Contact", l.ContactName)}{Line("Email", l.ContactEmail)}{Line("Phone", l.ContactPhone)}{Line("Source", l.Source)}\n" +
            $"## Request\n\n{l.Need}\n\n" +
            $"## Scope\n\n{Line("Business goal", l.BusinessGoal)}{Line("Target users", l.TargetUsers)}{Line("Platforms", l.Platforms)}{Line("Constraints", l.Constraints)}" +
            $"{Line("Budget", l.Budget?.ToString("N2"))}{Line("Desired date", l.DesiredDueDate?.ToString("yyyy-MM-dd"))}\n" +
            (qa.Count > 0 ? $"## Clarifications\n\n{string.Join("\n", qa)}" : "");
    }

    private async Task<Lead> LoadAsync(Guid id, uint? version, CancellationToken ct)
    {
        var l = await db.Leads.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lead", id);
        if (version is { } v) db.ExpectVersion(l, v);
        return l;
    }
}
