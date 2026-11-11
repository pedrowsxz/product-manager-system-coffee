using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Moinho.Web.Services;

// Identity uses one Data Protection token provider for everything by default. This second one only
// exists to give email-confirmation links a longer life (24 h) than password-reset links (2 h).
public class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions;

public class EmailConfirmationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<TUser>> logger)
    : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;
