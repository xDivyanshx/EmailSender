import { expect, test, type Page } from '@playwright/test'

const campaign = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Browser test campaign',
  createdAt: '2026-08-17T08:00:00Z',
  updatedAt: '2026-08-17T08:00:00Z',
  defaultSubject: 'Default subject',
  defaultTemplate: 'default',
  defaultAttachments: [],
  recipients: [
    { id: '21111111-1111-1111-1111-111111111111', name: 'Jane Doe', email: 'jane@example.com', organization: 'Example', subject: '', template: '', attachments: [], originalMessage: null, replyCheck: { status: 'unchecked' }, followUps: [] },
    { id: '31111111-1111-1111-1111-111111111111', name: 'John Roe', email: 'john@example.com', organization: 'Example', subject: '', template: '', attachments: [], originalMessage: null, replyCheck: { status: 'unchecked' }, followUps: [] },
  ],
}

async function mockWorkspace(page: Page) {
  let campaigns = [structuredClone(campaign)]
  const organizations = [{
    organization: 'Example', outcome: 'unreviewed', notes: '', contacts: 2, campaigns: 1, sent: 1, replied: 1, awaitingReply: 0, automated: 0, bounced: 0, followUps: 0,
    lastOutboundAt: '2026-08-17T08:00:00Z', lastReplyAt: '2026-08-18T08:00:00Z', lastCheckedAt: '2026-08-18T08:00:00Z',
    contactDetails: [{ campaignId: campaign.id, campaignName: campaign.name, recipientId: campaign.recipients[0].id, name: 'Jane Doe', email: 'jane@example.com', originalSent: true, lastOutboundAt: '2026-08-17T08:00:00Z', replyStatus: 'replied', replyCheckedAt: '2026-08-18T08:00:00Z', replyAvailable: true }],
  }]
  await page.route('**/api/**', async (route) => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path === '/api/gmail/status') return route.fulfill({ json: { credentialsConfigured: true, tokenStored: true, account: 'sender@example.com', status: 'connected-locally' } })
    if (path === '/api/templates') return route.fulfill({ json: ['default.html'] })
    if (/^\/api\/campaigns\/[^/]+\/attachments$/.test(path)) return route.fulfill({ json: [{ fileName: 'resume.pdf', mimeType: 'application/pdf', size: 1024 }] })
    if (path === '/api/batch/status') return route.fulfill({ json: { running: false, total: 0, processed: 0, sent: 0, failed: 0, skipped: 0, cancellationRequested: false } })
    if (path === '/api/settings' && request.method() === 'GET') return route.fulfill({ json: { senderName: 'Test Sender', defaultSubject: 'Default subject', defaultTemplate: 'default', dailyLimit: 400, gmailAccount: 'sender@example.com', sentLast24Hours: 12, sentTodayLocal: 7, sentTodayUtc: 9 } })
    if (path === '/api/settings' && request.method() === 'PUT') return route.fulfill({ json: { ...request.postDataJSON(), gmailAccount: 'sender@example.com', sentLast24Hours: 12, sentTodayLocal: 7, sentTodayUtc: 9 } })
    if (path === '/api/organizations' && request.method() === 'GET') return route.fulfill({ json: organizations })
    if (/^\/api\/campaigns\/[^/]+\/recipients\/[^/]+\/reply$/.test(path) && request.method() === 'GET') return route.fulfill({ json: { messageId: 'reply-1', threadId: 'thread-1', from: 'Jane Doe <jane@example.com>', to: 'sender@example.com', subject: 'Re: Default subject', receivedAt: '2026-08-18T08:00:00Z', plainTextBody: 'Thanks for reaching out.', sanitizedHtmlBody: '<p>Thanks for reaching out.</p>', attachments: [] } })
    if (path === '/api/original-emails/preview' && request.method() === 'POST') {
      const payload = request.postDataJSON() as { body?: string | null; subject?: string | null }
      return route.fulfill({ json: {
      selectedRecipients: 2,
      eligibleRecipients: 2,
      alreadySentRecipients: 0,
      remainingToday: 398,
      recipients: campaigns[0].recipients.map((recipient) => ({
        name: recipient.name,
        email: recipient.email,
        organization: recipient.organization,
        subject: payload.subject || 'Default subject',
        template: 'default',
        bodySource: payload.body ? 'manual-html' : 'default',
        renderedBody: payload.body || `<p>Hi ${recipient.name.split(' ')[0]}</p>`,
        attachments: [],
        alreadySent: false,
        resendApproved: false,
        eligible: true,
        errors: [],
      })),
    } })
    }
    if (path.endsWith('/follow-ups/preview') && request.method() === 'POST') return route.fulfill({ json: {
      selectedRecipients: 3,
      eligibleRecipients: 3,
      recipients: [
        { name: 'Jane Doe', email: 'jane@example.com', organization: 'Example', replyStatus: 'no-reply', lastOutboundAt: '2026-08-12T08:00:00Z', elapsedHours: 120, originalSubject: 'Default subject', subject: 'Re: Default subject', renderedBody: '<p>Hi Jane</p>', subjectChanged: false, attachments: [{ fileName: 'resume.pdf', exists: true }], eligible: true, errors: [] },
        { name: 'John Roe', email: 'john@example.com', organization: 'Example', replyStatus: 'replied', lastOutboundAt: '2026-08-15T08:00:00Z', elapsedHours: 48, originalSubject: 'Default subject', subject: 'Re: Default subject', renderedBody: '<p>Hi John</p>', subjectChanged: false, attachments: [], eligible: false, errors: ['Recipient replied.'] },
        { name: 'Asha Shah', email: 'asha@another.example', organization: 'Another Corp', replyStatus: 'no-reply', lastOutboundAt: '2026-08-10T08:00:00Z', elapsedHours: 168, originalSubject: 'Default subject', subject: 'Re: Default subject', renderedBody: '<p>Hi Asha</p>', subjectChanged: false, attachments: [], eligible: true, errors: [] },
      ],
    } })
    if (path === '/api/campaigns' && request.method() === 'GET') return route.fulfill({ json: campaigns })
    if (path.endsWith('/recipients/import') && request.method() === 'POST') {
      campaigns[0].recipients.push({ id: '61111111-1111-1111-1111-111111111111', name: 'CSV Person', email: 'csv@example.com', organization: 'Imported Corp', subject: '', template: '', presetName: '', attachments: [], originalMessage: null, replyCheck: { status: 'unchecked' }, followUps: [] })
      return route.fulfill({ json: { imported: 1 } })
    }
    if (path.endsWith('/recipients') && request.method() === 'POST') {
      const recipient = { id: '51111111-1111-1111-1111-111111111111', ...request.postDataJSON(), originalMessage: null, replyCheck: { status: 'unchecked' }, followUps: [] }
      campaigns[0].recipients.push(recipient)
      return route.fulfill({ status: 201, json: recipient })
    }
    if (path.endsWith('/duplicate') && request.method() === 'POST') {
      const copy = { ...structuredClone(campaigns[0]), id: '41111111-1111-1111-1111-111111111111', name: `${campaigns[0].name} Copy` }
      campaigns.push(copy)
      return route.fulfill({ status: 201, json: copy })
    }
    if (path.startsWith('/api/campaigns/') && request.method() === 'PUT') {
      campaigns[0].name = request.postDataJSON().name
      return route.fulfill({ json: campaigns[0] })
    }
    return route.fulfill({ status: 404, json: { error: `Unmocked browser-test route: ${request.method()} ${path}` } })
  })
}

test.beforeEach(async ({ page }) => {
  await mockWorkspace(page)
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Browser test campaign' })).toBeVisible()
})

test('loads the workspace and supports bulk recipient selection', async ({ page }) => {
  await expect(page.getByText('sender@example.com')).toBeVisible()
  await expect(page.getByText('2 selected')).toBeVisible()
  await page.getByRole('button', { name: 'Clear selection' }).click()
  await expect(page.getByText('0 selected')).toBeVisible()
  await page.getByRole('button', { name: 'Select all visible', exact: true }).click()
  await expect(page.getByText('2 selected')).toBeVisible()
  await page.getByRole('button', { name: 'Invert visible' }).click()
  await expect(page.getByText('0 selected')).toBeVisible()
  await page.getByLabel('Select first visible').selectOption('25')
  await expect(page.getByText('2 selected')).toBeVisible()
  await page.getByRole('button', { name: 'Selected only' }).click()
  await expect(page.getByRole('button', { name: 'Selected only' })).toHaveAttribute('aria-pressed', 'true')
  await page.getByPlaceholder('Search recipients').fill('Jane')
  await expect(page.getByText('jane@example.com')).toBeVisible()
  await expect(page.getByText('john@example.com')).toHaveCount(0)
  await expect(page.getByText('Repeated organizations: 1')).toBeVisible()
})

test('opens an organization reply inside the expanded organization row', async ({ page }) => {
  await page.getByRole('button', { name: 'Organizations' }).click()
  await page.getByTitle('Show contacts').click()
  await page.getByTitle('Open matched reply').click()

  const detailRow = page.locator('.organization-detail-row')
  await expect(detailRow.getByTitle('Organization reply content')).toBeVisible()
  await expect(detailRow.getByText('Re: Default subject')).toBeVisible()
  await expect(page.locator('.organization-tracker > .reply-viewer')).toHaveCount(0)
})

test('displays persisted reply and bounced counters after reload', async ({ page }) => {
  const persistedCampaign = structuredClone(campaign)
  persistedCampaign.recipients[0].replyCheck = { status: 'replied', checkedAt: '2026-08-18T08:00:00Z' }
  persistedCampaign.recipients[1].replyCheck = { status: 'bounced', checkedAt: '2026-08-18T08:00:00Z' }
  await page.route('**/api/campaigns', async (route) => route.fulfill({ json: [persistedCampaign] }))
  await page.reload()
  await expect(page.locator('.metrics > div').filter({ hasText: /^Replies/ })).toContainText('1')
  await expect(page.locator('.metrics > div').filter({ hasText: /^Bounced/ })).toContainText('1')
  await page.getByRole('button', { name: 'Reply check' }).click()
  await page.getByLabel('Reply classification').selectOption('replied')
  await expect(page.getByText('1 shown')).toBeVisible()
  await expect(page.getByText('jane@example.com')).toBeVisible()
  await expect(page.getByText('john@example.com')).toHaveCount(0)
})

test('shows campaign summary and prepares report downloads', async ({ page }) => {
  await page.getByRole('button', { name: 'Summary' }).click()
  await expect(page.getByRole('heading', { name: 'Campaign summary' })).toBeVisible()
  await expect(page.getByText('Recipients').first().locator('..').getByText('2')).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Example' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Markdown' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Full CSV' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Valid contacts CSV' })).toBeVisible()
})

test('applies a message preset without sending', async ({ page }) => {
  await page.getByLabel('Original message preset').selectOption({ label: 'Backend / .NET application' })
  await expect(page.getByLabel('Subject override')).toHaveValue('Application for Backend / .NET Software Engineer')
  await expect(page.getByLabel('HTML body override')).toContainText('{{organization}}')
  await expect(page.getByPlaceholder('Type SEND ORIGINAL EMAILS')).toHaveCount(0)
})

test('refreshes rendered HTML after editing an applied preset', async ({ page }) => {
  await page.getByLabel('Original message preset').selectOption({ label: 'Backend / .NET application' })
  await page.getByRole('button', { name: 'Preview batch' }).click()
  await page.getByTitle('Preview rendered HTML').first().click()
  await expect(page.getByTitle('Rendered original email')).toBeVisible()

  await page.getByLabel('HTML body override').fill('<p>Edited after applying preset</p>')
  await expect(page.getByTitle('Rendered original email')).toHaveCount(0)
  await page.getByRole('button', { name: 'Preview batch' }).click()
  await page.getByTitle('Preview rendered HTML').first().click()

  const frameBody = page.frameLocator('iframe[title="Rendered original email"]').locator('body')
  await expect(frameBody).toContainText('Edited after applying preset')
})

test('manages presets from the workspace section', async ({ page }) => {
  await page.getByRole('button', { name: 'Presets', exact: true }).click()
  await expect(page.locator('h2', { hasText: 'Message presets' })).toBeVisible()
  await page.getByRole('button', { name: 'Browser test campaign', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Original email batch' })).toBeVisible()
  await page.getByRole('button', { name: 'Presets', exact: true }).click()
  await expect(page.locator('h2', { hasText: 'Message presets' })).toBeVisible()
  await page.getByRole('button', { name: 'New preset' }).click()
  await page.getByLabel('Name', { exact: true }).fill('Test preset')
  await page.getByLabel('Original subject').fill('Test subject')
  await page.getByRole('button', { name: 'Save preset' }).click()
  await expect(page.getByText('Test preset', { exact: true })).toBeVisible()
  await page.getByTitle('Edit preset').last().click()
  await page.getByLabel('Name', { exact: true }).fill('Renamed preset')
  await page.getByRole('button', { name: 'Save preset' }).click()
  await expect(page.getByText('Renamed preset', { exact: true })).toBeVisible()
})

test('updates safe local settings through the UI', async ({ page }) => {
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  const senderName = page.getByLabel('Sender display name')
  await expect(senderName).toHaveValue('Test Sender')
  await senderName.fill('Updated Sender')
  await page.getByRole('button', { name: 'Save settings' }).click()
  await expect(page.getByText('Local settings saved and active')).toBeVisible()
  await expect(senderName).toHaveValue('Updated Sender')
  await expect(page.getByLabel('Connected Gmail account')).toHaveAttribute('readonly', '')
  await expect(page.getByText(/last 24 hours/)).toContainText('12')
  await expect(page.getByText(/today \(local\)/)).toContainText('7')
  await expect(page.getByText(/today \(UTC\)/)).toContainText('9')
})

test('renames and duplicates a campaign without invoking send routes', async ({ page }) => {
  page.once('dialog', (dialog) => dialog.accept('Renamed in browser'))
  await page.getByTitle('Rename campaign').click()
  await expect(page.getByRole('heading', { name: 'Renamed in browser' })).toBeVisible()
  await page.getByTitle('Duplicate campaign without history').click()
  await expect(page.getByRole('heading', { name: 'Renamed in browser Copy' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Renamed in browser Copy' })).toBeVisible()
})

test('isolates campaign-specific draft state when switching campaigns', async ({ page }) => {
  await page.getByLabel('Subject override').fill('Private campaign subject')
  await page.getByLabel('HTML body override').fill('<p>Private campaign body</p>')
  await page.getByPlaceholder('Search recipients').fill('Jane')
  await page.getByRole('button', { name: 'Clear selection' }).click()

  await page.getByTitle('Duplicate campaign without history').click()

  await expect(page.getByLabel('Subject override')).toHaveValue('')
  await expect(page.getByLabel('HTML body override')).toHaveValue('')
  await expect(page.getByPlaceholder('Search recipients')).toHaveValue('')
  await expect(page.getByText('2 selected')).toBeVisible()
})

test('requires preview and exact confirmation before enabling original send', async ({ page }) => {
  const sendButton = page.getByRole('button', { name: 'Send originals' })
  await expect(sendButton).toHaveCount(0)
  await page.getByRole('button', { name: 'Preview batch' }).click()
  await expect(page.getByText('2 eligible')).toBeVisible()
  await expect(sendButton).toBeDisabled()
  await page.getByPlaceholder('Type SEND ORIGINAL EMAILS').fill('send original emails')
  await expect(sendButton).toBeDisabled()
  await page.getByPlaceholder('Type SEND ORIGINAL EMAILS').fill('SEND ORIGINAL EMAILS')
  await expect(sendButton).toBeEnabled()
  await expect(page.getByText('Pre-send review')).toBeVisible()
  await expect(page.locator('.review-grid > div').filter({ hasText: /^Selected/ })).toContainText('2')
  await expect(page.locator('.review-grid > div').filter({ hasText: 'Remaining today' })).toContainText('398')
})

test('adds a recipient manually and imports a CSV through the UI', async ({ page }) => {
  await page.getByRole('button', { name: 'Add recipient' }).click()
  await page.getByLabel('Name', { exact: true }).fill('New Person')
  await page.getByLabel('Email', { exact: true }).fill('new@example.com')
  await page.getByLabel('Organization', { exact: true }).fill('New Corp')
  const recipientAttachment = page.locator('label').filter({ hasText: 'Recipient attachments' }).locator('button').filter({ hasText: 'resume.pdf' })
  await recipientAttachment.click()
  await expect(recipientAttachment).toHaveClass(/selected/)
  await expect(recipientAttachment).toHaveCSS('background-color', 'rgb(228, 242, 233)')
  await page.getByRole('button', { name: 'Save recipient' }).click()
  await expect(page.getByText('new@example.com')).toBeVisible()
  await expect(page.getByRole('row', { name: /New Person/ })).toContainText('resume.pdf')

  await page.locator('input[type="file"][accept*="csv"]').setInputFiles({
    name: 'recipients.csv',
    mimeType: 'text/csv',
    buffer: Buffer.from('Name,Organization,Email\nCSV Person,Imported Corp,csv@example.com\n'),
  })
  await expect(page.getByText('1 recipient imported.')).toBeVisible()
  await expect(page.getByText('csv@example.com')).toBeVisible()
})

test('filters follow-up candidates and composes selection across filters', async ({ page }) => {
  await page.getByRole('button', { name: 'Follow-ups' }).click()
  await page.getByRole('button', { name: 'Preview candidates' }).click()
  await expect(page.getByText('Attachments: resume.pdf')).toBeVisible()
  await expect(page.getByText('2 shown')).toBeVisible()
  await expect(page.getByRole('cell', { name: /Jane Doe jane@example.com/ })).toBeVisible()
  await expect(page.getByRole('cell', { name: /Asha Shah asha@another.example/ })).toBeVisible()
  await expect(page.getByText('john@example.com')).toHaveCount(0)

  const ageInput = page.locator('.filter-bar input[type="number"]')
  await page.locator('.age-shortcuts').getByRole('button', { name: '7d' }).click()
  await expect(ageInput).toHaveValue('7')
  await expect(page.locator('.age-shortcuts').getByRole('button', { name: '7d' })).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByText('1 shown')).toBeVisible()
  await page.locator('.age-shortcuts').getByRole('button', { name: '3d' }).click()
  await expect(ageInput).toHaveValue('3')
  await expect(page.getByText('2 shown')).toBeVisible()

  await page.getByLabel('Organization').selectOption('Example')
  await expect(page.getByText('1 shown')).toBeVisible()
  await expect(page.getByRole('cell', { name: /Jane Doe jane@example.com/ })).toBeVisible()
  await expect(page.getByText('asha@another.example')).toHaveCount(0)
  await page.getByRole('button', { name: 'Deselect all visible' }).click()
  await expect(page.getByText('1 selected from 1 visible eligible')).toBeVisible()
  await page.getByRole('button', { name: 'Select all visible', exact: true }).click()
  await expect(page.getByText('2 selected from 1 visible eligible')).toBeVisible()

  await page.getByLabel('Reply status').selectOption('all')
  await page.getByLabel('Show older than').fill('0')
  await expect(page.getByRole('cell', { name: /John Roe john@example.com/ })).toBeVisible()
})

test('persists a follow-up draft across reloads without restoring confirmation', async ({ page }) => {
  await page.getByRole('button', { name: 'Follow-ups' }).click()
  await page.getByLabel('HTML body').fill('<p>Persistent follow-up draft</p>')
  await page.getByLabel('Subject override').fill('Persistent subject')
  await page.getByRole('button', { name: 'Preview candidates' }).click()
  await page.getByPlaceholder('Type SEND FOLLOW-UP EMAILS').fill('SEND FOLLOW-UP EMAILS')
  await page.reload()
  await page.getByRole('button', { name: 'Follow-ups' }).click()
  await expect(page.getByLabel('HTML body')).toHaveValue('<p>Persistent follow-up draft</p>')
  await expect(page.getByLabel('Subject override')).toHaveValue('Persistent subject')
  await page.getByRole('button', { name: 'Preview candidates' }).click()
  await expect(page.getByPlaceholder('Type SEND FOLLOW-UP EMAILS')).toHaveValue('')
})
