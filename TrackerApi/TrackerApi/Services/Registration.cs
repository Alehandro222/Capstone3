using TrackerApi.Models;

namespace TrackerApi.Services;

// The registration form's rules and labels, shared by the phone, the desk and the kiosk.
public static class Registration
{
    public record Valid(string FirstName, string LastName, AgeGroup AgeGroup, Sex Sex,
        List<Sector> Sectors, List<ServiceAvailed> Services);

    // Checks every answer and returns a clean copy, or throws 400 with a message to show.
    public static Valid Check(RegistrationRequest r)
    {
        var first = r.FirstName?.Trim();
        var last = r.LastName?.Trim();

        if (string.IsNullOrEmpty(first)) throw TrackerException.BadRequest("Please enter your first name.");
        if (string.IsNullOrEmpty(last)) throw TrackerException.BadRequest("Please enter your last name.");
        if (first.Length > 100 || last.Length > 100)
            throw TrackerException.BadRequest("Names must be 100 characters or fewer.");
        if (r.AgeGroup is null || !Enum.IsDefined(r.AgeGroup.Value))
            throw TrackerException.BadRequest("Please choose your age group.");
        if (r.Sex is null || !Enum.IsDefined(r.Sex.Value))
            throw TrackerException.BadRequest("Please choose your sex.");
        if (r.Sectors is null || r.Sectors.Count == 0)
            throw TrackerException.BadRequest("Please select at least one sector.");
        if (r.Sectors.Any(s => !Enum.IsDefined(s)))
            throw TrackerException.BadRequest("Unknown sector.");
        if (r.Services is null || r.Services.Count == 0)
            throw TrackerException.BadRequest("Please select at least one purpose or service.");
        if (r.Services.Any(s => !Enum.IsDefined(s)))
            throw TrackerException.BadRequest("Unknown purpose or service.");

        return new Valid(first, last, r.AgeGroup.Value, r.Sex.Value,
            r.Sectors.Distinct().OrderBy(s => s).ToList(), r.Services.Distinct().OrderBy(s => s).ToList());
    }

    // How a session's purpose reads on screens: "Use of ICT equipment, eGov services".
    public static string ServicesText(IEnumerable<VisitServiceAvailed> services) =>
        string.Join(", ", services.Select(s => s.Service).OrderBy(s => s).Select(Label));

    public static string Label(ServiceAvailed service) => service switch
    {
        ServiceAvailed.Connectivity => "Connectivity",
        ServiceAvailed.IctEquipment => "Use of ICT equipment",
        ServiceAvailed.CoWorkingSpace => "Co-working space",
        ServiceAvailed.EgovServices => "eGov services",
        _ => "Others"
    };
}
