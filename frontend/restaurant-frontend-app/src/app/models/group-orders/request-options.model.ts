/** Options of the group pre-order requests that can run in the background. */
export interface GroupRequestOptions {
  /** Background refresh: the caller shows its own message, so no error toast is shown. */
  silent?: boolean;
}
