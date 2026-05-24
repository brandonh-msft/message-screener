using MessageScreener.Contracts;

namespace MessageScreener.Orchestration
{
    public sealed class TeamsTriggerPolicy : ITriggerPolicy
    {
        public TriggerEvaluationResult Evaluate(TeamsInboundMessage message)
        {
            if (message.Scope == ConversationScope.OneOnOne)
            {
                return new TriggerEvaluationResult(true, "one_on_one_always_review");
            }

            if (message.IsAtMention)
            {
                return new TriggerEvaluationResult(true, "group_mention_review");
            }

            return new TriggerEvaluationResult(false, "group_without_mention_no_review");
        }
    }
}