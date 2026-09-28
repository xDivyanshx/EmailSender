# Manual Test Checklist

Use this checklist only when ready to review the real local workspace. Automated tests do not replace the final visual and Gmail checks.

## Workspace Safety

- Confirm the application opens at `http://localhost:5195` and shows the intended Gmail account.
- Confirm no batch is already running and the daily remaining count is reasonable.
- Keep the first real verification batch limited to one controlled recipient.

## Campaign And Recipients

- Create a temporary campaign and confirm its name is readable.
- Rename, duplicate, export, and delete the temporary campaign. Confirm deleting local state does not affect Gmail.
- Add one recipient manually, edit it, and delete it.
- Download the sample CSV, import it, and confirm recipient fields and attachments are mapped correctly.
- Search recipients, clear selection, and select all visible rows.

## Settings And Attachments

- Open Settings and confirm sender name, subject, template, daily limit, and read-only Gmail account.
- Save a harmless settings change, refresh, and confirm it persists; restore the intended value afterward.
- Upload an attachment, select it, and confirm missing or invalid filenames block preview before sending.

## Original Email

- Enter the final subject and either an HTML body override or template name.
- Preview the controlled recipient and inspect rendered HTML, placeholders, subject, and attachment list.
- Confirm a previously sent address remains visible and requires explicit resend approval.
- Confirm the Send button stays disabled until `SEND ORIGINAL EMAILS` is typed exactly.
- Send only the controlled message, watch progress, and verify the completion counts and Gmail message.

## Replies And Follow-Ups

- Run Check replies and confirm it does not send email.
- Open a matched reply when available and inspect sender, subject, body, and attachment metadata.
- Preview follow-ups, then verify elapsed days/hours and organization filtering.
- Select only the intended controlled candidate and inspect the follow-up subject/body placeholders.
- Confirm the Send button requires `SEND FOLLOW-UP EMAILS` exactly.
- Send a controlled follow-up only when the recipient still has no reply, then verify it appears in the original Gmail thread.

## Final Review

- Confirm Activity shows the original and follow-up provider history.
- Confirm progress remains visible during a batch and the completion summary remains afterward.
- Confirm Help & Guide matches the current controls and placeholder behavior.
- Delete temporary campaigns and test attachments that are no longer referenced.
