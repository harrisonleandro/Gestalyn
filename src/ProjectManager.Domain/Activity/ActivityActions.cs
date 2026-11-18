namespace ProjectManager.Domain.Activity;

/// <summary>Catálogo fixo das ações registradas em <see cref="ActivityEvent"/> (ADR-014).</summary>
public static class ActivityActions
{
    public const string OrganizationCreated = "organization.created";
    public const string OrganizationUpdated = "organization.updated";
    public const string MemberInvited = "member.invited";
    public const string InvitationRevoked = "invitation.revoked";
    public const string InvitationAccepted = "invitation.accepted";
    public const string MemberRoleChanged = "member.role_changed";
    public const string MemberRemoved = "member.removed";

    public const string ProjectCreated = "project.created";
    public const string ProjectUpdated = "project.updated";
    public const string ProjectArchived = "project.archived";
    public const string ProjectMemberAdded = "project.member_added";
    public const string ProjectMemberUpdated = "project.member_updated";
    public const string ProjectMemberRemoved = "project.member_removed";
    public const string ProjectRepositoryAdded = "project.repository_added";
    public const string ProjectRepositoryRemoved = "project.repository_removed";

    public const string PhaseCreated = "phase.created";
    public const string PhaseUpdated = "phase.updated";
    public const string PhasesReordered = "phase.reordered";
    public const string PhaseDeleted = "phase.deleted";
    public const string MilestoneCreated = "milestone.created";
    public const string MilestoneUpdated = "milestone.updated";
    public const string MilestoneDeleted = "milestone.deleted";

    public const string TaskCreated = "task.created";
    public const string TaskUpdated = "task.updated";
    public const string TaskArchived = "task.archived";
    public const string TaskAssigneeAdded = "task.assignee_added";
    public const string TaskAssigneeRemoved = "task.assignee_removed";
    public const string TaskDependencyAdded = "task.dependency_added";
    public const string TaskDependencyRemoved = "task.dependency_removed";
    public const string TaskTransitioned = "task.transitioned";
    public const string TaskCommentAdded = "task.comment_added";
    public const string TaskCommentEdited = "task.comment_edited";
    public const string TaskCommentDeleted = "task.comment_deleted";
    public const string TaskAttachmentAdded = "task.attachment_added";
    public const string TaskAttachmentDeleted = "task.attachment_deleted";

    public const string WorkflowUpdated = "workflow.updated";
    public const string WorkflowStateAdded = "workflow.state_added";
    public const string WorkflowStateUpdated = "workflow.state_updated";
    public const string WorkflowStateMoved = "workflow.state_moved";
    public const string WorkflowStateDeleted = "workflow.state_deleted";
    public const string WorkflowTransitionAdded = "workflow.transition_added";
    public const string WorkflowTransitionRemoved = "workflow.transition_removed";

    public const string DocumentCreated = "document.created";
    public const string DocumentUpdated = "document.updated";
    public const string DocumentArchived = "document.archived";
    public const string DocumentRestored = "document.restored";
    public const string DecisionProposed = "decision.proposed";
    public const string DecisionUpdated = "decision.updated";
    public const string DecisionAccepted = "decision.accepted";
    public const string DecisionRejected = "decision.rejected";
    public const string DecisionSuperseded = "decision.superseded";

    public const string AgentCreated = "agent.created";
    public const string AgentUpdated = "agent.updated";
    public const string AgentDisabled = "agent.disabled";
    public const string AgentEnabled = "agent.enabled";
    public const string AgentCredentialCreated = "agent.credential_created";
    public const string AgentCredentialRevoked = "agent.credential_revoked";
    public const string AgentProjectAccessGranted = "agent.project_access_granted";
    public const string AgentProjectAccessUpdated = "agent.project_access_updated";
    public const string AgentProjectAccessRemoved = "agent.project_access_removed";
}

/// <summary>Tipos de entidade dos eventos.</summary>
public static class ActivityEntityTypes
{
    public const string Organization = "organization";
    public const string Member = "member";
    public const string Invitation = "invitation";
    public const string Project = "project";
    public const string ProjectMember = "project_member";
    public const string Repository = "repository";
    public const string Phase = "phase";
    public const string Milestone = "milestone";
    public const string Task = "task";
    public const string Comment = "comment";
    public const string Attachment = "attachment";
    public const string Workflow = "workflow";
    public const string WorkflowState = "workflow_state";
    public const string WorkflowTransition = "workflow_transition";
    public const string Document = "document";
    public const string Decision = "decision";
    public const string Agent = "agent";
    public const string AgentCredential = "agent_credential";
    public const string AgentProjectAccess = "agent_project_access";
}
