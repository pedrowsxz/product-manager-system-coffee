using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moinho.Web.Models;

namespace Moinho.Web.Areas.Admin.Controllers;

// Every Admin controller inherits from this, so none of them can forget the area or the policy.
// AdminAuthorizationTests checks this by reflection.
[Area("Admin")]
[Authorize(Policy = Policies.RequireAdmin)]
public abstract class AdminControllerBase : Controller;
