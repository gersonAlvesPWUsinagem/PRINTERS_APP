namespace PRINTERS_APP.Models;

public sealed record LoginAppResponse(
    string Token,
    string Service,
    string Ipv4,
    int TargetOSType,
    string CodeSession);
