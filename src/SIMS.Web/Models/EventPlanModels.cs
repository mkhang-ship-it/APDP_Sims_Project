using SIMS.Application.EventPlans;
using SIMS.Domain.Events;

namespace SIMS.Web.Models;

public sealed class EventPlanPageViewModel
{
    public Event Event { get; init; } = null!;

    public EventPlanView? Plan { get; init; }
}
