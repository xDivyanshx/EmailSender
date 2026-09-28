import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Check,
  Building2,
  ChevronRight,
  CircleAlert,
  Copy,
  FileUp,
  Download,
  Eye,
  Paperclip,
  Inbox,
  Mail,
  Plus,
  RefreshCw,
  Search,
  Send,
  Settings,
  Trash2,
  Pencil,
  Users,
  X,
} from 'lucide-react'
import './App.css'
import { guideSections, guideUpdated, placeholders } from './guide'

type GmailStatus = {
  credentialsConfigured: boolean
  tokenStored: boolean
  account: string
  status: string
  error?: string | null
  authorizationExpiresAt?: string | null
}

type AttachmentFile = {
  fileName: string
  size: number
  lastModified: string
}

type Delivery = {
  sentAt: string
  subject: string
  attachments: string[]
  providerMessageId?: string | null
  providerThreadId?: string | null
}

type CampaignRecipient = {
  id: string
  name: string
  email: string
  organization: string
  subject: string
  template: string
  presetName?: string
  attachments: string[]
  originalMessage?: Delivery | null
  replyCheck: {
    checkedAt?: string | null
    status: string
    matchedMessageId?: string | null
    notes?: string | null
  }
  followUps: Delivery[]
}

type Campaign = {
  id: string
  name: string
  createdAt: string
  updatedAt: string
  defaultSubject: string
  defaultTemplate: string
  defaultAttachments: string[]
  recipients: CampaignRecipient[]
}

type PreviewRecipient = {
  name: string
  email: string
  organization: string
  subject: string
  template: string
  bodySource: string
  renderedBody: string
  attachments: { fileName: string; exists: boolean }[]
  alreadySent: boolean
  previouslySentAt?: string | null
  resendApproved: boolean
  eligible: boolean
  errors: string[]
}

type OriginalPreview = {
  selectedRecipients: number
  eligibleRecipients: number
  alreadySentRecipients: number
  remainingToday: number
  recipients: PreviewRecipient[]
}

type FollowUpPreviewRecipient = {
  name: string
  email: string
  organization: string
  replyStatus: string
  lastOutboundAt: string
  elapsedHours: number
  originalSubject: string
  subject: string
  renderedBody: string
  attachments: { fileName: string; exists: boolean }[]
  subjectChanged: boolean
  eligible: boolean
  errors: string[]
}

type FollowUpPreview = {
  selectedRecipients: number
  eligibleRecipients: number
  recipients: FollowUpPreviewRecipient[]
}

type ReplyView = {
  messageId: string
  threadId?: string | null
  from: string
  to: string
  subject: string
  receivedAt?: string | null
  plainTextBody: string
  sanitizedHtmlBody: string
  attachments: { fileName: string; mimeType: string; size: number }[]
}

type BatchStatus = {
  running: boolean
  operationType?: string | null
  total: number
  processed: number
  sent: number
  failed: number
  skipped: number
  currentEmail?: string | null
  cancellationRequested: boolean
}

type ApplicationSettings = {
  senderName: string
  defaultSubject: string
  defaultTemplate: string
  dailyLimit: number
  gmailAccount: string
  sentLast24Hours: number
  sentTodayLocal: number
  sentTodayUtc: number
}

type OrganizationContact = {
  campaignId: string
  campaignName: string
  recipientId: string
  name: string
  email: string
  originalSent: boolean
  lastOutboundAt?: string | null
  replyStatus: string
  replyCheckedAt?: string | null
  replyAvailable: boolean
}

type OrganizationSummary = {
  organization: string
  outcome: OrganizationOutcome
  notes: string
  trackingUpdatedAt?: string | null
  contacts: number
  campaigns: number
  sent: number
  replied: number
  awaitingReply: number
  automated: number
  bounced: number
  followUps: number
  lastOutboundAt?: string | null
  lastReplyAt?: string | null
  lastCheckedAt?: string | null
  contactDetails: OrganizationContact[]
}

type Tab = 'original' | 'replies' | 'followups' | 'summary' | 'activity' | 'organizations' | 'presets' | 'templates' | 'settings' | 'help'
type OrganizationOutcome = 'unreviewed' | 'no-response' | 'interested' | 'not-interested' | 'follow-up' | 'other'
type ReplyClassificationFilter = 'all' | 'replied' | 'no-reply' | 'automated' | 'bounced' | 'uncertain' | 'missing-thread' | 'not-checked'
type FollowReplyFilter = 'eligible' | ReplyClassificationFilter

type FollowUpDraft = {
  body: string
  subject: string
  attachments: string
  allowSubjectChange: boolean
  olderDays: number
  organization: string
  replyStatus: FollowReplyFilter
}
type MessagePreset = { name: string; subject: string; body: string; attachments: string; followSubject: string; followBody: string; followAttachments: string }

const defaultFollowBody = '<p>Hi {{name}},</p><p>I wanted to follow up on my previous email.</p>'
const defaultPresets: MessagePreset[] = [
  { name: 'General software application', subject: '', body: '', attachments: '', followSubject: '', followBody: defaultFollowBody, followAttachments: '' },
  { name: 'Backend / .NET application', subject: 'Application for Backend / .NET Software Engineer', body: '<p>Hi {{name}},</p><p>I am writing to express my interest in software engineering opportunities at {{organization}}.</p>', attachments: '', followSubject: '', followBody: defaultFollowBody, followAttachments: '' },
  { name: 'Recruiter outreach', subject: 'Software engineering opportunity', body: '<p>Hi {{name}},</p><p>I would appreciate being considered for relevant software engineering opportunities at {{organization}}.</p>', attachments: '', followSubject: '', followBody: defaultFollowBody, followAttachments: '' },
]

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error ?? data?.title ?? `Request failed (${response.status})`)
  return data as T
}

function formatDate(value?: string | null) {
  if (!value) return 'Not yet'
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function statusLabel(value: string) {
  return value.replaceAll('-', ' ')
}

function csvCell(value: unknown) {
  const text = value == null ? '' : String(value)
  return `"${text.replaceAll('"', '""')}"`
}

function safeFileName(value: string) {
  return value.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'campaign'
}

function downloadText(fileName: string, content: string, type: string) {
  const url = URL.createObjectURL(new Blob([content], { type }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}

function App() {
  const [gmail, setGmail] = useState<GmailStatus | null>(null)
  const [attachmentFiles, setAttachmentFiles] = useState<AttachmentFile[]>([])
  const [templates, setTemplates] = useState<string[]>([])
  const [campaigns, setCampaigns] = useState<Campaign[]>([])
  const [selectedCampaignId, setSelectedCampaignId] = useState<string | null>(null)
  const [selectedEmails, setSelectedEmails] = useState<Set<string>>(new Set())
  const [showSelectedOnly, setShowSelectedOnly] = useState(false)
  const [tab, setTab] = useState<Tab>('original')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [newCampaignName, setNewCampaignName] = useState('')
  const [showCreate, setShowCreate] = useState(false)
  const [search, setSearch] = useState('')
  const [showRecipientForm, setShowRecipientForm] = useState(false)
  const [editingRecipientId, setEditingRecipientId] = useState<string | null>(null)
  const [recipientDraft, setRecipientDraft] = useState({ name: '', email: '', organization: '', subject: '', template: '', attachments: '' })
  const [subject, setSubject] = useState('')
  const [originalBody, setOriginalBody] = useState('')
  const [attachments, setAttachments] = useState('')
  const [allowResend, setAllowResend] = useState(false)
  const [originalPreview, setOriginalPreview] = useState<OriginalPreview | null>(null)
  const [originalConfirmation, setOriginalConfirmation] = useState('')
  const [originalBodyPreview, setOriginalBodyPreview] = useState<PreviewRecipient | null>(null)
  const [followBody, setFollowBody] = useState(defaultFollowBody)
  const [followSubject, setFollowSubject] = useState('')
  const [followAttachments, setFollowAttachments] = useState('')
  const [allowSubjectChange, setAllowSubjectChange] = useState(false)
  const [followPreview, setFollowPreview] = useState<FollowUpPreview | null>(null)
  const [followBodyPreview, setFollowBodyPreview] = useState<FollowUpPreviewRecipient | null>(null)
  const [followSelection, setFollowSelection] = useState<Set<string>>(new Set())
  const [followConfirmation, setFollowConfirmation] = useState('')
  const [followOlderDays, setFollowOlderDays] = useState(3)
  const [followOrganization, setFollowOrganization] = useState('all')
  const [followReplyFilter, setFollowReplyFilter] = useState<FollowReplyFilter>('eligible')
  const [replyView, setReplyView] = useState<ReplyView | null>(null)
  const [replyClassificationFilter, setReplyClassificationFilter] = useState<ReplyClassificationFilter>('all')
  const [guideSearch, setGuideSearch] = useState('')
  const [batchStatus, setBatchStatus] = useState<BatchStatus | null>(null)
  const [settings, setSettings] = useState<ApplicationSettings | null>(null)
  const [settingsDraft, setSettingsDraft] = useState<ApplicationSettings | null>(null)
  const [organizations, setOrganizations] = useState<OrganizationSummary[]>([])
  const [organizationSearch, setOrganizationSearch] = useState('')
  const [organizationOutcomeFilter, setOrganizationOutcomeFilter] = useState<OrganizationOutcome | 'all'>('all')
  const [organizationDrafts, setOrganizationDrafts] = useState<Record<string, { outcome: OrganizationOutcome; notes: string }>>({})
  const [expandedOrganization, setExpandedOrganization] = useState<string | null>(null)
  const [organizationReplyView, setOrganizationReplyView] = useState<ReplyView | null>(null)
  const [organizationReplyTarget, setOrganizationReplyTarget] = useState<string | null>(null)
  const [exportCampaignIds, setExportCampaignIds] = useState<Set<string>>(new Set())
  const [exportCampaignSearch, setExportCampaignSearch] = useState('')
  const [presets, setPresets] = useState<MessagePreset[]>(defaultPresets)
  const [editingPresetName, setEditingPresetName] = useState<string | null>(null)
  const [presetDraft, setPresetDraft] = useState<MessagePreset>({ name: '', subject: '', body: '', attachments: '', followSubject: '', followBody: defaultFollowBody, followAttachments: '' })
  const fileInput = useRef<HTMLInputElement>(null)
  const attachmentInput = useRef<HTMLInputElement>(null)
  const templateInput = useRef<HTMLInputElement>(null)
  const initializedCampaignId = useRef<string | null>(null)

  useEffect(() => {
    try {
      const storedV2 = localStorage.getItem('message-presets-v2')
      if (storedV2) setPresets(JSON.parse(storedV2) as MessagePreset[])
      else {
        const stored = localStorage.getItem('message-presets')
        if (stored) setPresets([...defaultPresets, ...(JSON.parse(stored) as MessagePreset[])])
      }
    } catch { /* use built-ins */ }
  }, [])

  function applyPreset(preset: MessagePreset) {
    setSubject(preset.subject); setOriginalBody(preset.body); setAttachments(preset.attachments)
    setFollowSubject(preset.followSubject); setFollowBody(preset.followBody); setFollowAttachments(preset.followAttachments)
    setOriginalPreview(null); setOriginalConfirmation(''); setFollowPreview(null); setFollowConfirmation('')
    setNotice(`Preset “${preset.name}” applied. Review the populated fields before previewing.`)
  }

  function savePreset() {
    const name = presetDraft.name.trim()
    if (!name) return
    const saved = { ...presetDraft, name }
    const next = [...presets.filter((preset) => preset.name !== editingPresetName && preset.name !== name), saved]
    setPresets(next)
    localStorage.setItem('message-presets-v2', JSON.stringify(next))
    setEditingPresetName(name)
    setPresetDraft(saved)
    setNotice(`Preset “${name}” saved locally.`)
  }

  function editPreset(preset: MessagePreset) { setEditingPresetName(preset.name); setPresetDraft({ ...preset }) }
  function newPreset() { setEditingPresetName(null); setPresetDraft({ name: '', subject: '', body: '', attachments: '', followSubject: '', followBody: defaultFollowBody, followAttachments: '' }) }
  function deletePreset(name: string) {
    if (!window.confirm(`Delete preset ${name}?`)) return
    const next = presets.filter((preset) => preset.name !== name)
    setPresets(next); localStorage.setItem('message-presets-v2', JSON.stringify(next)); newPreset(); setNotice(`Preset “${name}” deleted.`)
  }

  const selectedCampaign = useMemo(
    () => campaigns.find((campaign) => campaign.id === selectedCampaignId) ?? null,
    [campaigns, selectedCampaignId],
  )
  const exportCampaignOptions = useMemo(() => campaigns.filter(campaign => campaign.name.toLowerCase().includes(exportCampaignSearch.trim().toLowerCase())), [campaigns, exportCampaignSearch])

  const refresh = useCallback(async () => {
    setError(null)
    try {
      const [gmailData, campaignData, attachmentData, templateData, batchData, settingsData, organizationData] = await Promise.all([
        api<GmailStatus>('/api/gmail/status'),
        api<Campaign[]>('/api/campaigns'),
        selectedCampaignId ? api<AttachmentFile[]>(`/api/campaigns/${selectedCampaignId}/attachments`) : Promise.resolve([]),
        api<string[]>('/api/templates'),
        api<BatchStatus>('/api/batch/status'),
        api<ApplicationSettings>('/api/settings'),
        api<OrganizationSummary[]>('/api/organizations'),
      ])
      setGmail(gmailData)
      setCampaigns(campaignData)
      setAttachmentFiles(attachmentData)
      setTemplates(templateData)
      setBatchStatus(batchData)
      setSettings(settingsData)
      setSettingsDraft((current) => current ?? settingsData)
      setOrganizations(organizationData)
      setOrganizationDrafts((current) => {
        const next = { ...current }
        organizationData.forEach((organization) => {
          if (!next[organization.organization]) next[organization.organization] = { outcome: organization.outcome, notes: organization.notes }
        })
        return next
      })
      setSelectedCampaignId((current) =>
        current && campaignData.some((campaign) => campaign.id === current)
          ? current
          : campaignData[0]?.id ?? null,
      )
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Could not load application data.')
    } finally {
      setLoading(false)
    }
  }, [selectedCampaignId])

  const visibleOrganizations = useMemo(() => {
    const query = organizationSearch.trim().toLowerCase()
    return organizations.filter((organization) =>
      (organizationOutcomeFilter === 'all' || organization.outcome === organizationOutcomeFilter)
      && (!query || organization.organization.toLowerCase().includes(query)
        || organization.notes.toLowerCase().includes(query)
        || organization.contactDetails.some((contact) => contact.name.toLowerCase().includes(query) || contact.email.toLowerCase().includes(query))))
  }, [organizations, organizationOutcomeFilter, organizationSearch])
  const organizationsLastCheckedAt = useMemo(() => {
    const timestamps = organizations.flatMap((organization) => organization.lastCheckedAt ? [+new Date(organization.lastCheckedAt)] : [])
    return timestamps.length ? new Date(Math.max(...timestamps)).toISOString() : null
  }, [organizations])

  async function saveOrganizationTracking(organization: OrganizationSummary) {
    const draft = organizationDrafts[organization.organization] ?? { outcome: organization.outcome, notes: organization.notes }
    await runAction(`organization-${organization.organization}`, async () => {
      const updated = await api<OrganizationSummary>('/api/organizations/tracking', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ organization: organization.organization, outcome: draft.outcome, notes: draft.notes }),
      })
      setOrganizations((current) => current.map((item) => item.organization === updated.organization ? updated : item))
      setNotice(`${updated.organization} tracking updated.`)
    })
  }

  async function openOrganizationReply(contact: OrganizationContact) {
    await runAction(`organization-reply-${contact.recipientId}`, async () => {
      setOrganizationReplyView(await api<ReplyView>(`/api/campaigns/${contact.campaignId}/recipients/${contact.recipientId}/reply`))
      setOrganizationReplyTarget(`${contact.campaignId}-${contact.recipientId}`)
    })
  }

  async function checkAllReplies() {
    await runAction('check-all-replies', async () => {
      const result = await api<{ campaigns: number; totalRecipients: number; replied: number; noReply: number; automated: number; bounced: number; completedAt: string }>(
        '/api/organizations/replies/check',
        { method: 'POST' },
      )
      await refresh()
      setNotice(`Checked ${result.totalRecipients} recipients across ${result.campaigns} campaigns. ${result.replied} replied, ${result.noReply} awaiting, ${result.automated} automated, ${result.bounced} bounced.`)
    })
  }

  function exportOrganizationsCsv() {
    const headers = ['Organization', 'Outcome', 'Notes', 'Contacts', 'Campaigns', 'Sent', 'Replied', 'Awaiting Reply', 'Automated', 'Bounced', 'Follow-ups', 'Last Outbound', 'Last Reply', 'Last Checked']
    const rows = visibleOrganizations.map((organization) => [organization.organization, organization.outcome, organization.notes, organization.contacts, organization.campaigns, organization.sent, organization.replied, organization.awaitingReply, organization.automated, organization.bounced, organization.followUps, organization.lastOutboundAt ?? '', organization.lastReplyAt ?? '', organization.lastCheckedAt ?? ''])
    downloadText('organization-tracker.csv', [headers, ...rows].map((row) => row.map(csvCell).join(',')).join('\r\n'), 'text/csv;charset=utf-8')
  }

  async function reloadTemplates() {
    await runAction('reload-templates', async () => {
      const result = await api<{ templates: number; names: string[] }>('/api/templates/reload', { method: 'POST' })
      setTemplates(result.names)
      setNotice(`Reloaded ${result.templates} HTML template${result.templates === 1 ? '' : 's'}.`)
    })
  }

  async function uploadTemplate(file: File) {
    await runAction('upload-template', async () => { const form = new FormData(); form.append('file', file); const result = await api<{ names: string[] }>('/api/templates/upload', { method: 'POST', body: form }); setTemplates(result.names); setNotice(`Uploaded ${file.name} and reloaded templates.`) })
  }

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    if (busy !== 'send-original' && busy !== 'send-followup') return
    const poll = async () => {
      try { setBatchStatus(await api<BatchStatus>('/api/batch/status')) } catch { /* The main request reports errors. */ }
    }
    void poll()
    const timer = window.setInterval(() => void poll(), 500)
    return () => window.clearInterval(timer)
  }, [busy])

  const campaignStats = useMemo(() => {
    const list = selectedCampaign?.recipients ?? []
    return {
      sent: list.filter((recipient) => recipient.originalMessage).length,
      replied: list.filter((recipient) => recipient.replyCheck.status === 'replied').length,
      noReply: list.filter((recipient) => recipient.replyCheck.status === 'no-reply').length,
      bounced: list.filter((recipient) => recipient.replyCheck.status === 'bounced').length,
      followUps: list.reduce((sum, recipient) => sum + recipient.followUps.length, 0),
    }
  }, [selectedCampaign])

  const campaignReport = useMemo(() => {
    const recipients = selectedCampaign?.recipients ?? []
    const companies = new Map<string, { name: string; contacts: number; sent: number; replied: number; awaiting: number; bounced: number }>()
    recipients.forEach((recipient) => {
      const name = recipient.organization.trim() || 'Unspecified'
      const key = name.toLowerCase()
      const row = companies.get(key) ?? { name, contacts: 0, sent: 0, replied: 0, awaiting: 0, bounced: 0 }
      row.contacts += 1
      if (recipient.originalMessage) row.sent += 1
      if (recipient.replyCheck.status === 'replied') row.replied += 1
      if (recipient.replyCheck.status === 'no-reply') row.awaiting += 1
      if (recipient.replyCheck.status === 'bounced') row.bounced += 1
      companies.set(key, row)
    })
    const validContacts = [...new Map(recipients
      .filter((recipient) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(recipient.email.trim()))
      .filter((recipient) => recipient.replyCheck.status !== 'bounced')
      .map((recipient) => [recipient.email.trim().toLowerCase(), recipient])).values()]
    return {
      total: recipients.length,
      companies: [...companies.values()].sort((a, b) => a.name.localeCompare(b.name)),
      sent: recipients.filter((recipient) => recipient.originalMessage).length,
      unsent: recipients.filter((recipient) => !recipient.originalMessage).length,
      replied: recipients.filter((recipient) => recipient.replyCheck.status === 'replied').length,
      awaiting: recipients.filter((recipient) => recipient.replyCheck.status === 'no-reply').length,
      automated: recipients.filter((recipient) => recipient.replyCheck.status === 'automated').length,
      bounced: recipients.filter((recipient) => recipient.replyCheck.status === 'bounced').length,
      uncertain: recipients.filter((recipient) => ['uncertain', 'missing-thread'].includes(recipient.replyCheck.status)).length,
      followUps: recipients.reduce((sum, recipient) => sum + recipient.followUps.length, 0),
      validContacts,
    }
  }, [selectedCampaign])

  const originalReview = useMemo(() => {
    if (!originalPreview) return null
    const recipients = originalPreview.recipients
    const errors = recipients.flatMap((recipient) => recipient.errors)
    const duplicateCount = new Set(recipients.map((recipient) => recipient.email.trim().toLowerCase())).size
    return {
      selected: originalPreview.selectedRecipients,
      eligible: originalPreview.eligibleRecipients,
      previouslySent: originalPreview.alreadySentRecipients,
      invalid: errors.filter((error) => /email|address|format/i.test(error)).length,
      missingAttachments: errors.filter((error) => /attachment/i.test(error)).length,
      blocked: recipients.filter((recipient) => !recipient.eligible).length,
      duplicates: recipients.length - duplicateCount,
      replied: recipients.filter((recipient) => /repl(y|ied)/i.test(recipient.errors.join(' '))).length,
      remainingToday: originalPreview.remainingToday,
    }
  }, [originalPreview])

  const recipientWarnings = useMemo(() => {
    const selected = (selectedCampaign?.recipients ?? []).filter((recipient) => selectedEmails.has(recipient.email))
    const allEmails = new Map<string, number>()
    selected.forEach((recipient) => allEmails.set(recipient.email.trim().toLowerCase(), (allEmails.get(recipient.email.trim().toLowerCase()) ?? 0) + 1))
    const duplicateEmails = [...allEmails.values()].filter((count) => count > 1).length
    const crossCampaign = selected.filter((recipient) => campaigns.some((campaign) => campaign.id !== selectedCampaignId && campaign.recipients.some((other) => other.email.trim().toLowerCase() === recipient.email.trim().toLowerCase()))).length
    const recentCutoff = Date.now() - 7 * 24 * 60 * 60 * 1000
    const recentlySent = selected.filter((recipient) => recipient.originalMessage?.sentAt && new Date(recipient.originalMessage.sentAt).getTime() >= recentCutoff).length
    const replied = selected.filter((recipient) => recipient.replyCheck.status === 'replied').length
    const bounced = selected.filter((recipient) => recipient.replyCheck.status === 'bounced').length
    const organizations = new Map<string, number>()
    selected.forEach((recipient) => { const key = recipient.organization.trim().toLowerCase(); if (key) organizations.set(key, (organizations.get(key) ?? 0) + 1) })
    const repeatedOrganizations = [...organizations.values()].filter((count) => count > 1).length
    return { duplicateEmails, crossCampaign, recentlySent, replied, bounced, repeatedOrganizations }
  }, [campaigns, selectedCampaign, selectedCampaignId, selectedEmails])

  const visibleReplyRecipients = useMemo(() => {
    const recipients = selectedCampaign?.recipients ?? []
    return replyClassificationFilter === 'all' ? recipients : recipients.filter((recipient) => recipient.replyCheck.status === replyClassificationFilter)
  }, [replyClassificationFilter, selectedCampaign])

  useEffect(() => {
    if (initializedCampaignId.current === selectedCampaignId) return
    initializedCampaignId.current = selectedCampaignId
    const recipients = campaigns.find((campaign) => campaign.id === selectedCampaignId)?.recipients ?? []
    const campaign = campaigns.find((item) => item.id === selectedCampaignId)
    setSelectedEmails(new Set(recipients.map((recipient) => recipient.email)))
    setSearch('')
    setShowSelectedOnly(false)
    setShowRecipientForm(false)
    setEditingRecipientId(null)
    setRecipientDraft({ name: '', email: '', organization: '', subject: '', template: '', attachments: '' })
    setSubject('')
    setOriginalBody('')
    setAttachments(campaign?.defaultAttachments.join(', ') ?? '')
    setAllowResend(false)
    setOriginalPreview(null)
    setOriginalConfirmation('')
    setOriginalBodyPreview(null)
    let followDraft: FollowUpDraft | null = null
    try {
      const storedDraft = localStorage.getItem(`follow-up-draft:${selectedCampaignId}`)
      followDraft = storedDraft ? JSON.parse(storedDraft) as FollowUpDraft : null
    } catch {
      followDraft = null
    }
    setFollowBody(followDraft?.body ?? defaultFollowBody)
    setFollowSubject(followDraft?.subject ?? '')
    setFollowAttachments(followDraft?.attachments ?? '')
    setAllowSubjectChange(followDraft?.allowSubjectChange ?? false)
    setFollowPreview(null)
    setFollowSelection(new Set())
    setFollowConfirmation('')
    setReplyClassificationFilter('all')
    setFollowOlderDays(followDraft?.olderDays ?? 3)
    setFollowOrganization(followDraft?.organization ?? 'all')
    setFollowReplyFilter(followDraft?.replyStatus ?? 'eligible')
    setReplyView(null)
  }, [campaigns, selectedCampaignId])

  const searchedRecipients = (selectedCampaign?.recipients ?? []).filter((recipient) =>
    `${recipient.name} ${recipient.email} ${recipient.organization}`.toLowerCase().includes(search.toLowerCase()),
  )
  const filteredRecipients = showSelectedOnly
    ? searchedRecipients.filter((recipient) => selectedEmails.has(recipient.email))
    : searchedRecipients

  const followOrganizations = useMemo(
    () => [...new Set((followPreview?.recipients ?? []).map((recipient) => recipient.organization).filter(Boolean))].sort(),
    [followPreview],
  )

  const visibleFollowRecipients = useMemo(
    () => (followPreview?.recipients ?? []).filter((recipient) =>
      recipient.elapsedHours >= followOlderDays * 24
      && (followOrganization === 'all' || recipient.organization === followOrganization)
      && (followReplyFilter === 'all'
        || (followReplyFilter === 'eligible' ? recipient.eligible : recipient.replyStatus === followReplyFilter)),
    ),
    [followPreview, followOlderDays, followOrganization, followReplyFilter],
  )

  const visibleGuideSections = useMemo(() => {
    const query = guideSearch.trim().toLowerCase()
    if (!query) return guideSections
    return guideSections.filter((section) => `${section.title} ${section.items.join(' ')}`.toLowerCase().includes(query))
  }, [guideSearch])

  useEffect(() => {
    if (!selectedCampaignId || initializedCampaignId.current !== selectedCampaignId) return
    const timer = window.setTimeout(() => {
      const draft: FollowUpDraft = {
        body: followBody,
        subject: followSubject,
        attachments: followAttachments,
        allowSubjectChange,
        olderDays: followOlderDays,
        organization: followOrganization,
        replyStatus: followReplyFilter,
      }
      localStorage.setItem(`follow-up-draft:${selectedCampaignId}`, JSON.stringify(draft))
    }, 0)
    return () => window.clearTimeout(timer)
  }, [selectedCampaignId, followBody, followSubject, followAttachments, allowSubjectChange, followOlderDays, followOrganization, followReplyFilter])

  useEffect(() => {
    setOriginalBodyPreview(null)
  }, [originalBody, subject, attachments, allowResend])

  useEffect(() => {
    setFollowBodyPreview(null)
  }, [followBody, followSubject, followAttachments, allowSubjectChange])

  function attachmentList(value: string) {
    return value.split(',').map((item) => item.trim()).filter(Boolean)
  }

  function formatBytes(value: number) {
    if (value < 1024) return `${value} B`
    if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`
    return `${(value / (1024 * 1024)).toFixed(1)} MB`
  }

  function toggleAttachment(fileName: string, value: string, setter: (next: string) => void) {
    const selected = new Set(attachmentList(value))
    if (selected.has(fileName)) selected.delete(fileName)
    else selected.add(fileName)
    setter([...selected].join(', '))
  }

  async function toggleCampaignAttachment(fileName: string) {
    if (!selectedCampaign) return
    const selected = new Set(attachmentList(attachments))
    if (selected.has(fileName)) selected.delete(fileName)
    else selected.add(fileName)
    const next = [...selected]
    setAttachments(next.join(', '))
    await runAction('save-campaign-attachments', async () => {
      await api(`/api/campaigns/${selectedCampaign.id}/defaults`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          defaultSubject: selectedCampaign.defaultSubject,
          defaultTemplate: selectedCampaign.defaultTemplate,
          defaultAttachments: next,
        }),
      })
      await refresh()
    })
  }

  async function uploadAttachment(file: File) {
    await runAction('upload-attachment', async () => {
      const exists = attachmentFiles.some((attachment) => attachment.fileName.toLowerCase() === file.name.toLowerCase())
      const overwrite = exists && window.confirm(`${file.name} already exists. Replace it?`)
      if (exists && !overwrite) return
      const form = new FormData()
      form.append('file', file)
      if (!selectedCampaign) return
      await api(`/api/campaigns/${selectedCampaign.id}/attachments?overwrite=${overwrite}`, { method: 'POST', body: form })
      await refresh()
      setNotice(`${file.name} uploaded.`)
    })
  }

  async function deleteAttachment(fileName: string) {
    if (!window.confirm(`Delete attachment ${fileName}?`)) return
    await runAction('delete-attachment', async () => {
      if (!selectedCampaign) return
      await api(`/api/campaigns/${selectedCampaign.id}/attachments/${encodeURIComponent(fileName)}`, { method: 'DELETE' })
      await refresh()
      setNotice(`${fileName} deleted.`)
    })
  }

  function formatElapsed(hours: number) {
    const wholeHours = Math.max(0, Math.floor(hours))
    const days = Math.floor(wholeHours / 24)
    const remainder = wholeHours % 24
    if (!days) return `${remainder}h`
    return `${days}d ${remainder}h`
  }

  async function runAction(name: string, action: () => Promise<void>) {
    setBusy(name)
    setError(null)
    setNotice(null)
    try {
      await action()
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'The operation failed.')
    } finally {
      setBusy(null)
    }
  }

  async function createCampaign() {
    if (!newCampaignName.trim()) return
    await runAction('create', async () => {
      const campaign = await api<Campaign>('/api/campaigns', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: newCampaignName, defaultSubject: null, defaultTemplate: null, defaultAttachments: [] }),
      })
      setNewCampaignName('')
      setShowCreate(false)
      await refresh()
      setSelectedCampaignId(campaign.id)
      setNotice('Campaign created.')
    })
  }

  async function saveSettings() {
    if (!settingsDraft) return
    await runAction('save-settings', async () => {
      const saved = await api<ApplicationSettings>('/api/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settingsDraft),
      })
      setSettings(saved)
      setSettingsDraft(saved)
      setNotice('Local settings saved and active for future previews and sends.')
    })
  }

  async function renameCampaign() {
    if (!selectedCampaign) return
    const name = window.prompt('Campaign name', selectedCampaign.name)?.trim()
    if (!name || name === selectedCampaign.name) return
    await runAction('rename-campaign', async () => {
      await api(`/api/campaigns/${selectedCampaign.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }) })
      await refresh()
      setNotice('Campaign renamed.')
    })
  }

  async function duplicateCampaign() {
    if (!selectedCampaign) return
    await runAction('duplicate-campaign', async () => {
      const copy = await api<Campaign>(`/api/campaigns/${selectedCampaign.id}/duplicate`, { method: 'POST' })
      await refresh()
      setSelectedCampaignId(copy.id)
      setNotice('Campaign duplicated without delivery or reply history.')
    })
  }

  async function exportCampaign() {
    if (!selectedCampaign) return
    const response = await fetch(`/api/campaigns/${selectedCampaign.id}/export`)
    if (!response.ok) { setError('Could not export campaign.'); return }
    const url = URL.createObjectURL(await response.blob())
    const link = document.createElement('a')
    link.href = url
    link.download = `${selectedCampaign.name.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'campaign'}.json`
    link.click()
    URL.revokeObjectURL(url)
  }

  function exportMarkdownReport() {
    if (!selectedCampaign) return
    const report = campaignReport
    const lines = [`# ${selectedCampaign.name} Campaign Report`, '', `Generated: ${new Date().toISOString()}`, '', '## Summary', '', `- Recipients: ${report.total}`, `- Unique companies: ${report.companies.length}`, `- Sent: ${report.sent}`, `- Not sent: ${report.unsent}`, `- Human replies: ${report.replied}`, `- Awaiting reply: ${report.awaiting}`, `- Automated replies: ${report.automated}`, `- Bounced: ${report.bounced}`, `- Uncertain or missing thread: ${report.uncertain}`, `- Follow-ups sent: ${report.followUps}`, `- Valid shareable contacts: ${report.validContacts.length}`, '', '## Company Breakdown', '', '| Company | Contacts | Sent | Replied | Awaiting | Bounced |', '|---|---:|---:|---:|---:|---:|', ...report.companies.map((company) => `| ${company.name.replaceAll('|', '\\|')} | ${company.contacts} | ${company.sent} | ${company.replied} | ${company.awaiting} | ${company.bounced} |`), '', '## Recipients', '', '| Name | Email | Company | Sent | Reply status | Follow-ups |', '|---|---|---|---|---|---:|', ...selectedCampaign.recipients.map((recipient) => `| ${recipient.name.replaceAll('|', '\\|')} | ${recipient.email} | ${recipient.organization.replaceAll('|', '\\|')} | ${recipient.originalMessage ? 'Yes' : 'No'} | ${statusLabel(recipient.replyCheck.status)} | ${recipient.followUps.length} |`)]
    downloadText(`${safeFileName(selectedCampaign.name)}-report.md`, lines.join('\n'), 'text/markdown;charset=utf-8')
  }

  function exportCampaignCsv() {
    if (!selectedCampaign) return
    const headers = ['Name', 'Email', 'Organization', 'Subject', 'Sent', 'Sent At', 'Reply Status', 'Reply Checked At', 'Follow-ups Sent', 'Last Follow-up At', 'Attachments', 'Template']
    const rows = selectedCampaign.recipients.map((recipient) => [recipient.name, recipient.email, recipient.organization, recipient.originalMessage?.subject || recipient.subject || selectedCampaign.defaultSubject, recipient.originalMessage ? 'Yes' : 'No', recipient.originalMessage?.sentAt ?? '', recipient.replyCheck.status, recipient.replyCheck.checkedAt ?? '', recipient.followUps.length, recipient.followUps.at(-1)?.sentAt ?? '', recipient.attachments.join('|'), recipient.template || selectedCampaign.defaultTemplate])
    downloadText(`${safeFileName(selectedCampaign.name)}-recipients.csv`, [headers, ...rows].map((row) => row.map(csvCell).join(',')).join('\r\n'), 'text/csv;charset=utf-8')
  }

  function exportValidContactsCsv() {
    if (!selectedCampaign) return
    const rows = campaignReport.validContacts.map((recipient) => [recipient.name, recipient.email, recipient.organization])
    downloadText(`${safeFileName(selectedCampaign.name)}-valid-contacts.csv`, [['Name', 'Email', 'Organization'], ...rows].map((row) => row.map(csvCell).join(',')).join('\r\n'), 'text/csv;charset=utf-8')
  }

  function exportWorkspaceCsv(validOnly: boolean) {
    const selected = campaigns.filter(c => exportCampaignIds.has(c.id)); const all = selected.flatMap(c => c.recipients)
    const rows = (validOnly ? [...new Map(all.filter(r => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(r.email) && r.replyCheck.status !== 'bounced').map(r => [r.email.toLowerCase(), r])).values()] : all)
    const headers = validOnly ? ['Name','Email','Organization'] : ['Name','Email','Organization','Subject','Sent','Sent At','Reply Status','Reply Checked At','Follow-ups Sent','Last Follow-up At','Attachments','Template']
    const data = rows.map(r => validOnly ? [r.name,r.email,r.organization] : [r.name,r.email,r.organization,r.originalMessage?.subject || r.subject,'',r.originalMessage?.sentAt ?? '',r.replyCheck.status,r.replyCheck.checkedAt ?? '',r.followUps.length,r.followUps.at(-1)?.sentAt ?? '',r.attachments.join('|'),r.template])
    downloadText(`workspace-${validOnly ? 'valid-contacts' : 'recipients'}.csv`, [headers,...data].map(row=>row.map(csvCell).join(',')).join('\r\n'),'text/csv;charset=utf-8')
  }

  function exportWorkspaceMarkdown() { const all=campaigns.filter(c=>exportCampaignIds.has(c.id)).flatMap(c=>c.recipients); downloadText('workspace-report.md', [`# Workspace Campaign Report`,'',`Generated: ${new Date().toISOString()}`,'',`- Recipients: ${all.length}`,`- Sent: ${all.filter(r=>r.originalMessage).length}`,`- Replies: ${all.filter(r=>r.replyCheck.status==='replied').length}`].join('\n'),'text/markdown;charset=utf-8') }

  async function deleteCampaign() {
    if (!selectedCampaign) return
    if (window.prompt(`Type DELETE CAMPAIGN to delete ${selectedCampaign.name}. Gmail messages are not deleted.`) !== 'DELETE CAMPAIGN') return
    await runAction('delete-campaign', async () => {
      await api(`/api/campaigns/${selectedCampaign.id}`, { method: 'DELETE' })
      setSelectedCampaignId(null)
      await refresh()
      setNotice('Local campaign deleted. Gmail messages were not changed.')
    })
  }

  async function importCsv(file: File) {
    if (!selectedCampaign) {
      setError('Create or select a campaign before importing recipients.')
      return
    }
    await runAction('import', async () => {
      const form = new FormData()
      form.append('file', file)
      const result = await api<{ imported: number }>(`/api/campaigns/${selectedCampaign.id}/recipients/import`, { method: 'POST', body: form })
      await refresh()
      setOriginalPreview(null)
      setNotice(`${result.imported} recipient${result.imported === 1 ? '' : 's'} imported.`)
    })
  }

  const originalPayload = {
    campaignId: selectedCampaign?.id ?? null,
    emails: [...selectedEmails],
    subject: subject || null,
    body: originalBody || null,
    attachments: attachmentList(attachments),
    recipientOverrides: selectedCampaign?.recipients.filter((recipient) => selectedEmails.has(recipient.email) && recipient.presetName).map((recipient) => {
      const preset = presets.find((item) => item.name === recipient.presetName)
      return { email: recipient.email, subject: preset?.subject || null, body: preset?.body || null, attachments: attachmentList(preset?.attachments ?? '') }
    }) ?? [],
    allowResend,
  }

  function openNewRecipient() {
    setEditingRecipientId(null)
    setRecipientDraft({ name: '', email: '', organization: '', subject: '', template: '', attachments: '' })
    setShowRecipientForm(true)
  }

  function openEditRecipient(recipient: CampaignRecipient) {
    setEditingRecipientId(recipient.id)
    setRecipientDraft({
      name: recipient.name,
      email: recipient.email,
      organization: recipient.organization,
      subject: recipient.subject,
      template: recipient.template,
      attachments: recipient.attachments.join(', '),
    })
    setShowRecipientForm(true)
  }

  async function saveRecipient() {
    if (!selectedCampaign) return
    await runAction('save-recipient', async () => {
      const body = JSON.stringify({
        name: recipientDraft.name,
        email: recipientDraft.email,
        organization: recipientDraft.organization,
        subject: recipientDraft.subject || null,
        template: recipientDraft.template || null,
        attachments: attachmentList(recipientDraft.attachments),
      })
      await api(
        editingRecipientId
          ? `/api/campaigns/${selectedCampaign.id}/recipients/${editingRecipientId}`
          : `/api/campaigns/${selectedCampaign.id}/recipients`,
        { method: editingRecipientId ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body },
      )
      setShowRecipientForm(false)
      await refresh()
      setNotice(editingRecipientId ? 'Recipient updated.' : 'Recipient added.')
    })
  }

  async function deleteRecipient(recipient: CampaignRecipient) {
    if (!selectedCampaign || !window.confirm(`Delete ${recipient.email} from this campaign?`)) return
    await runAction('delete-recipient', async () => {
      await api(`/api/campaigns/${selectedCampaign.id}/recipients/${recipient.id}`, { method: 'DELETE' })
      await refresh()
      setNotice('Recipient deleted.')
    })
  }

  async function previewOriginal() {
    const missingPresets = selectedCampaign?.recipients
      .filter((recipient) => selectedEmails.has(recipient.email) && recipient.presetName && !presets.some((preset) => preset.name === recipient.presetName))
      .map((recipient) => `${recipient.email}: ${recipient.presetName}`) ?? []
    if (missingPresets.length) {
      setError(`PresetName not found in this browser: ${missingPresets.join(', ')}`)
      return
    }
    await runAction('preview-original', async () => {
      const preview = await api<OriginalPreview>('/api/original-emails/preview', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(originalPayload),
      })
      setOriginalPreview(preview)
      setNotice('Preview refreshed. No email was sent.')
    })
  }

  async function sendOriginals() {
    if (!selectedCampaign) return
    await runAction('send-original', async () => {
      const result = await api<{ sent: number; failed: number; results?: Array<{ email: string; success: boolean; status: string; error?: string }> }>('/api/original-emails/send', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ...originalPayload,
          confirm: true,
          confirmationText: originalConfirmation,
        }),
      })
      setOriginalConfirmation('')
      setBatchStatus(await api<BatchStatus>('/api/batch/status'))
      setOriginalPreview(null)
      await refresh()
      const failures = result.results?.filter((item) => !item.success && item.error).map((item) => `${item.email}: ${item.error}`) ?? []
      setNotice(failures.length ? `${result.sent} sent, ${result.failed} failed. ${failures.join(' | ')}` : `${result.sent} sent, ${result.failed} failed.`)
    })
  }

  async function checkReplies() {
    if (!selectedCampaign) return
    await runAction('check-replies', async () => {
      const result = await api<{ replied: number; noReply: number; automated: number; bounced: number }>(
        `/api/campaigns/${selectedCampaign.id}/replies/check`,
        { method: 'POST' },
      )
      await refresh()
      setNotice(`${result.replied} replied, ${result.noReply} awaiting reply, ${result.automated} automated, ${result.bounced} bounced.`)
    })
  }

  async function openReply(recipient: CampaignRecipient) {
    if (!selectedCampaign) return
    await runAction(`reply-${recipient.id}`, async () => {
      const message = await api<ReplyView>(`/api/campaigns/${selectedCampaign.id}/recipients/${recipient.id}/reply`)
      setReplyView(message)
    })
  }

  const followPayload = {
    emails: [...followSelection],
    body: followBody,
    subject: followSubject || null,
    attachments: attachmentList(followAttachments),
    recipientOverrides: [],
    allowSubjectChange,
  }

  async function previewFollowUps(selectCandidates: unknown = false) {
    if (!selectedCampaign) return
    await runAction('preview-followup', async () => {
      const preview = await api<FollowUpPreview>(`/api/campaigns/${selectedCampaign.id}/follow-ups/preview`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...followPayload, emails: followSelection.size ? [...followSelection] : null }),
      })
      setFollowPreview(preview)
      if (selectCandidates === true) {
        setFollowSelection(new Set(preview.recipients
          .filter((recipient) => recipient.eligible)
          .filter((recipient) => recipient.elapsedHours >= followOlderDays * 24)
          .filter((recipient) => followOrganization === 'all' || recipient.organization === followOrganization)
          .filter((recipient) => followReplyFilter === 'all'
            || (followReplyFilter === 'eligible' ? recipient.eligible : recipient.replyStatus === followReplyFilter))
          .map((recipient) => recipient.email)))
      }
      setFollowBodyPreview(null)
      setNotice('Follow-up preview refreshed. No email was sent.')
    })
  }

  async function sendFollowUps() {
    if (!selectedCampaign) return
    await runAction('send-followup', async () => {
      const result = await api<{ sent: number; failed: number; skipped: number }>(
        `/api/campaigns/${selectedCampaign.id}/follow-ups/send`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...followPayload, confirm: true, confirmationText: followConfirmation }),
        },
      )
      setFollowConfirmation('')
      setBatchStatus(await api<BatchStatus>('/api/batch/status'))
      setFollowPreview(null)
      await refresh()
      setNotice(`${result.sent} follow-up${result.sent === 1 ? '' : 's'} sent, ${result.failed} failed, ${result.skipped} skipped.`)
    })
  }

  async function cancelBatch() {
    try {
      await api('/api/batch/cancel', { method: 'POST' })
      setBatchStatus(await api<BatchStatus>('/api/batch/status'))
      setNotice('Cancellation requested. The current Gmail call will finish first.')
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : 'Could not cancel the batch.')
    }
  }

  function toggleEmail(email: string, target: Set<string>, setter: (value: Set<string>) => void) {
    const next = new Set(target)
    if (next.has(email)) next.delete(email)
    else next.add(email)
    setter(next)
  }

  function selectFirstVisible(count: number) {
    setSelectedEmails((current) => new Set([...current, ...filteredRecipients.slice(0, count).map((recipient) => recipient.email)]))
  }

  function invertVisibleSelection() {
    setSelectedEmails((current) => {
      const next = new Set(current)
      filteredRecipients.forEach((recipient) => {
        if (next.has(recipient.email)) next.delete(recipient.email)
        else next.add(recipient.email)
      })
      return next
    })
  }

  if (loading) return <div className="loading-screen"><RefreshCw className="spin" /> Loading workspace</div>

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand"><Mail size={20} /><span>Cold Mail</span></div>
        <div className="sidebar-section-label">Campaigns</div>
        <nav className="campaign-list" aria-label="Campaigns">
          {campaigns.map((campaign) => (
            <button
              key={campaign.id}
              className={campaign.id === selectedCampaignId ? 'campaign-link active' : 'campaign-link'}
              onClick={() => { setSelectedCampaignId(campaign.id); setTab('original') }}
            >
              <span>{campaign.name}</span><ChevronRight size={15} />
            </button>
          ))}
          {!campaigns.length && <p className="empty-copy">No campaigns yet.</p>}
        </nav>
        {showCreate ? (
          <div className="create-campaign">
            <input value={newCampaignName} onChange={(event) => setNewCampaignName(event.target.value)} placeholder="Campaign name" autoFocus />
            <div><button className="button primary compact" onClick={createCampaign} disabled={busy === 'create'}><Check size={15} /> Create</button><button className="button compact" onClick={() => setShowCreate(false)}>Cancel</button></div>
          </div>
        ) : (
          <button className="new-campaign" onClick={() => setShowCreate(true)}><Plus size={16} /> New campaign</button>
        )}
        <div className="workspace-nav"><div className="sidebar-section-label">Workspace</div><button className={tab === 'organizations' ? 'workspace-link active' : 'workspace-link'} onClick={() => setTab('organizations')}><Building2 size={16} /> Organizations</button><button className={tab === 'presets' ? 'workspace-link active' : 'workspace-link'} onClick={() => setTab('presets')}><Copy size={16} /> Presets</button><button className={tab === 'templates' ? 'workspace-link active' : 'workspace-link'} onClick={() => setTab('templates')}><FileUp size={16} /> Templates</button><button className={tab === 'settings' ? 'workspace-link active' : 'workspace-link'} onClick={() => setTab('settings')}><Settings size={16} /> Settings</button><button className={tab === 'help' ? 'workspace-link active' : 'workspace-link'} onClick={() => setTab('help')}><CircleAlert size={16} /> Help & Guide</button></div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <div>
            <h1>{tab === 'organizations' ? 'Organization tracker' : tab === 'presets' ? 'Message presets' : tab === 'templates' ? 'HTML templates' : tab === 'settings' ? 'Workspace settings' : tab === 'help' ? 'Help & Guide' : selectedCampaign?.name ?? 'Campaign workspace'}</h1>
            <p>{tab === 'organizations' ? `${organizations.length} organization${organizations.length === 1 ? '' : 's'} across all campaigns` : tab === 'templates' ? `${templates.length} template${templates.length === 1 ? '' : 's'} loaded` : tab === 'presets' ? `${presets.length} reusable preset${presets.length === 1 ? '' : 's'}` : tab === 'settings' ? `${exportCampaignIds.size} campaign${exportCampaignIds.size === 1 ? '' : 's'} selected for export` : tab === 'help' ? 'Local workspace reference' : selectedCampaign ? `Updated ${formatDate(selectedCampaign.updatedAt)}` : 'Create a campaign to begin'}</p>
          </div>
          <div className="topbar-actions">
            {selectedCampaign && !['organizations', 'presets', 'templates', 'settings', 'help'].includes(tab) && <div className="campaign-actions"><button className="icon-button" title="Rename campaign" onClick={() => void renameCampaign()}><Pencil size={16} /></button><button className="icon-button" title="Duplicate campaign without history" onClick={() => void duplicateCampaign()}><Copy size={16} /></button><button className="icon-button" title="Export campaign JSON" onClick={() => void exportCampaign()}><Download size={16} /></button><button className="icon-button destructive" title="Delete local campaign" onClick={() => void deleteCampaign()}><Trash2 size={16} /></button></div>}
            <div className={`connection ${gmail?.tokenStored && gmail.status !== 'reauthorization-required' ? 'connected' : 'disconnected'}`}><span />{gmail?.status === 'reauthorization-required' ? 'Gmail login expired' : gmail?.tokenStored ? gmail.account : 'Gmail disconnected'}</div>
            <button className="button" onClick={() => runAction('connect', async () => { await api('/api/gmail/connect', { method: 'POST' }); await refresh() })} disabled={busy !== null}>{gmail?.tokenStored && gmail.status !== 'reauthorization-required' ? 'Reconnect Gmail' : 'Connect Gmail'}</button>
            <button className="icon-button" title="Refresh workspace" onClick={() => void refresh()}><RefreshCw size={17} /></button>
          </div>
        </header>

        {error && <div className="alert error"><CircleAlert size={17} /><span>{error}</span><button onClick={() => setError(null)}>Dismiss</button></div>}
        {notice && <div className="alert success"><Check size={17} /><span>{notice}</span><button onClick={() => setNotice(null)}>Dismiss</button></div>}
        {batchStatus && batchStatus.total > 0 && <div className={`batch-progress ${batchStatus.running ? '' : 'completed'}`}><div className="batch-progress-heading"><div><strong>{statusLabel(batchStatus.operationType ?? 'batch')} {batchStatus.running ? 'in progress' : 'completed'}</strong><span>{batchStatus.processed} of {batchStatus.total}{batchStatus.currentEmail ? ` · ${batchStatus.currentEmail}` : ''}</span></div>{batchStatus.running ? <button className="button compact" onClick={() => void cancelBatch()} disabled={batchStatus.cancellationRequested}>{batchStatus.cancellationRequested ? 'Cancelling' : 'Cancel batch'}</button> : <button className="button compact" onClick={() => setBatchStatus(null)}>Dismiss</button>}</div><div className="progress-track"><span style={{ width: `${batchStatus.total ? (batchStatus.processed / batchStatus.total) * 100 : 0}%` }} /></div><small>{batchStatus.sent} sent · {batchStatus.failed} failed · {batchStatus.skipped} skipped</small></div>}

        {!['organizations', 'presets', 'templates', 'settings', 'help'].includes(tab) && <section className="metrics" aria-label="Campaign summary">
          <div><span>Originals sent</span><strong>{campaignStats.sent}</strong></div>
          <div><span>Replies</span><strong>{campaignStats.replied}</strong></div>
          <div><span>Awaiting reply</span><strong>{campaignStats.noReply}</strong></div>
          <div><span>Bounced</span><strong>{campaignStats.bounced}</strong></div>
          <div><span>Follow-ups sent</span><strong>{campaignStats.followUps}</strong></div>
        </section>}

        {!['organizations', 'presets', 'templates', 'settings', 'help'].includes(tab) && <div className="tabs" role="tablist">
          {([['original', 'Original email'], ['replies', 'Reply check'], ['followups', 'Follow-ups'], ['summary', 'Summary'], ['activity', 'Activity']] as [Tab, string][]).map(([value, label]) => (
            <button key={value} className={tab === value ? 'active' : ''} onClick={() => setTab(value)}>{label}</button>
          ))}
        </div>}

        {tab === 'templates' && <section className="panel"><div className="panel-heading"><div><h2>HTML templates</h2><p>Shared workspace templates used by campaign CSV rows.</p></div><div className="heading-actions"><input ref={templateInput} type="file" accept=".html,text/html" hidden onChange={(event) => event.target.files?.[0] && void uploadTemplate(event.target.files[0])} /><button className="button" onClick={() => templateInput.current?.click()} disabled={busy !== null}><FileUp size={16} /> Upload template</button><button className="button" onClick={() => void reloadTemplates()} disabled={busy !== null}><RefreshCw size={16} /> Reload</button></div></div><div className="template-files page-template-files">{templates.map((template) => <code key={template}>{template}</code>)}{!templates.length && <span className="library-empty">No HTML templates found.</span>}</div></section>}
        {tab === 'organizations' && (
          <section className="panel organization-tracker">
            <div className="panel-heading"><div><h2>Organization outcomes</h2><p>Aggregated across every local campaign. Last workspace check: {formatDate(organizationsLastCheckedAt)}.</p></div><div className="heading-actions"><button className="button primary" onClick={() => void checkAllReplies()} disabled={busy !== null}><RefreshCw size={16} className={busy === 'check-all-replies' ? 'spin' : ''} /> Check all replies</button><button className="button" onClick={exportOrganizationsCsv} disabled={!visibleOrganizations.length}><Download size={16} /> Export CSV</button></div></div>
            <div className="organization-tools"><div className="search"><Search size={15} /><input value={organizationSearch} onChange={(event) => setOrganizationSearch(event.target.value)} placeholder="Search organizations or contacts" /></div><label><span>Outcome</span><select value={organizationOutcomeFilter} onChange={(event) => setOrganizationOutcomeFilter(event.target.value as OrganizationOutcome | 'all')}><option value="all">All outcomes</option><option value="unreviewed">Unreviewed</option><option value="no-response">No response</option><option value="interested">Interested</option><option value="not-interested">Not interested</option><option value="follow-up">Follow up</option><option value="other">Other</option></select></label><span>{visibleOrganizations.length} shown</span></div>
            <div className="table-wrap"><table><thead><tr><th>Organization</th><th>Outcome</th><th>Activity</th><th>Replies</th><th>Last outbound</th><th>Last checked</th><th>Notes</th><th className="actions-cell">Actions</th></tr></thead><tbody>
              {visibleOrganizations.map((organization) => {
                const draft = organizationDrafts[organization.organization] ?? { outcome: organization.outcome, notes: organization.notes }
                const expanded = expandedOrganization === organization.organization
                const organizationReplyOpen = expanded && organizationReplyView && organization.contactDetails.some((contact) => `${contact.campaignId}-${contact.recipientId}` === organizationReplyTarget)
                return <Fragment key={organization.organization}><tr><td><strong>{organization.organization}</strong><small>{organization.contacts} contact{organization.contacts === 1 ? '' : 's'} across {organization.campaigns} campaign{organization.campaigns === 1 ? '' : 's'}</small></td><td><select className="compact-select organization-outcome" value={draft.outcome} onChange={(event) => setOrganizationDrafts((current) => ({ ...current, [organization.organization]: { ...draft, outcome: event.target.value as OrganizationOutcome } }))}><option value="unreviewed">Unreviewed</option><option value="no-response">No response</option><option value="interested">Interested</option><option value="not-interested">Not interested</option><option value="follow-up">Follow up</option><option value="other">Other</option></select></td><td><strong>{organization.sent} sent</strong><small>{organization.followUps} follow-up{organization.followUps === 1 ? '' : 's'} · {organization.bounced} bounced</small></td><td><strong>{organization.replied} replied</strong><small>{organization.awaitingReply} awaiting · {organization.automated} automated</small></td><td>{formatDate(organization.lastOutboundAt)}</td><td>{formatDate(organization.lastCheckedAt)}</td><td><textarea className="organization-notes" rows={2} value={draft.notes} onChange={(event) => setOrganizationDrafts((current) => ({ ...current, [organization.organization]: { ...draft, notes: event.target.value } }))} placeholder="Decision context or next step" /></td><td className="actions-cell"><button className="row-action" title={expanded ? 'Hide contacts' : 'Show contacts'} onClick={() => { setExpandedOrganization(expanded ? null : organization.organization); if (expanded) { setOrganizationReplyView(null); setOrganizationReplyTarget(null) } }}><Eye size={14} /></button><button className="row-action" title="Save organization tracking" onClick={() => void saveOrganizationTracking(organization)} disabled={busy !== null}><Check size={14} /></button></td></tr>{expanded && <tr className="organization-detail-row"><td colSpan={8}><div className="organization-contacts">{organization.contactDetails.map((contact) => <div key={`${contact.campaignId}-${contact.recipientId}`}><div><strong>{contact.name}</strong><span>{contact.email} · {contact.campaignName}</span></div><div><span className={`status ${contact.replyStatus}`}>{statusLabel(contact.replyStatus)}</span><small>{contact.originalSent ? `Outbound ${formatDate(contact.lastOutboundAt)} · checked ${formatDate(contact.replyCheckedAt)}` : `Original not sent · checked ${formatDate(contact.replyCheckedAt)}`}</small></div>{contact.replyAvailable && <button className="row-action" title="Open matched reply" onClick={() => void openOrganizationReply(contact)} disabled={busy !== null}><Eye size={14} /></button>}</div>)}</div>{organizationReplyOpen && <div className="reply-viewer organization-reply-viewer"><div className="reply-viewer-header"><div><strong>{organizationReplyView.subject || '(No subject)'}</strong><span>{organizationReplyView.from} · {formatDate(organizationReplyView.receivedAt)}</span></div><button className="icon-button" title="Close reply" onClick={() => { setOrganizationReplyView(null); setOrganizationReplyTarget(null) }}><X size={16} /></button></div>{organizationReplyView.sanitizedHtmlBody ? <iframe title="Organization reply content" sandbox="" srcDoc={organizationReplyView.sanitizedHtmlBody} /> : <pre>{organizationReplyView.plainTextBody || 'This reply has no readable body.'}</pre>}</div>}</td></tr>}</Fragment>
              })}
              {!visibleOrganizations.length && <tr><td colSpan={8} className="empty-row">No organizations match these filters.</td></tr>}
            </tbody></table></div>
          </section>
        )}
        {tab === 'original' && (
          <section className="panel">
            <div className="panel-heading"><div><h2>Original email batch</h2><p>Add recipients manually or import CSV, review final content, then send.</p></div><div className="heading-actions"><button className="button" onClick={openNewRecipient} disabled={!selectedCampaign}><Plus size={16} /> Add recipient</button><a className="button" href="/sample_recipients.csv" download><Download size={16} /> Sample CSV</a><input ref={fileInput} type="file" accept=".csv,text/csv" hidden onChange={(event) => event.target.files?.[0] && void importCsv(event.target.files[0])} /><button className="button" onClick={() => fileInput.current?.click()} disabled={busy === 'import' || !selectedCampaign}><FileUp size={16} /> Import CSV</button></div></div>
            <div className="attachment-library"><div><strong>Attachment library</strong><span>Files are stored locally in resources/attachments.</span></div><input ref={attachmentInput} type="file" hidden onChange={(event) => event.target.files?.[0] && void uploadAttachment(event.target.files[0])} /><button className="button" onClick={() => attachmentInput.current?.click()} disabled={busy !== null}><Paperclip size={16} /> Upload attachment</button><div className="attachment-files">{attachmentFiles.map((attachment) => <div className="attachment-file" key={attachment.fileName}><Paperclip size={14} /><span><strong>{attachment.fileName}</strong><small>{formatBytes(attachment.size)}</small></span><button className="row-action destructive" title="Delete attachment" onClick={() => void deleteAttachment(attachment.fileName)}><Trash2 size={13} /></button></div>)}{!attachmentFiles.length && <span className="library-empty">No managed attachments uploaded.</span>}</div></div>
            {showRecipientForm && <div className="recipient-editor"><div className="editor-title"><strong>{editingRecipientId ? 'Edit recipient' : 'Add recipient'}</strong><span>Saved directly to this campaign.</span></div><div className="recipient-fields"><label><span>Name</span><input value={recipientDraft.name} onChange={(event) => setRecipientDraft({ ...recipientDraft, name: event.target.value })} /></label><label><span>Email</span><input type="email" value={recipientDraft.email} onChange={(event) => setRecipientDraft({ ...recipientDraft, email: event.target.value })} /></label><label><span>Organization</span><input value={recipientDraft.organization} onChange={(event) => setRecipientDraft({ ...recipientDraft, organization: event.target.value })} /></label><label><span>Custom subject</span><input value={recipientDraft.subject} onChange={(event) => setRecipientDraft({ ...recipientDraft, subject: event.target.value })} /></label><label><span>Custom template</span><input value={recipientDraft.template} onChange={(event) => setRecipientDraft({ ...recipientDraft, template: event.target.value })} placeholder="Optional template name" /></label><label><span>Recipient attachments</span><div className="attachment-picker">{attachmentFiles.map((file) => <button type="button" key={file.fileName} className={attachmentList(recipientDraft.attachments).includes(file.fileName) ? 'selected' : ''} onClick={() => toggleAttachment(file.fileName, recipientDraft.attachments, (value) => setRecipientDraft({ ...recipientDraft, attachments: value }))}><Paperclip size={13} />{file.fileName}</button>)}</div></label></div><div className="editor-actions"><button className="button" onClick={() => setShowRecipientForm(false)}>Cancel</button><button className="button primary" onClick={saveRecipient} disabled={!recipientDraft.name || !recipientDraft.email || !recipientDraft.organization || busy !== null}><Check size={16} /> Save recipient</button></div></div>}
            <div className="form-grid">
              <div className="preset-bar wide"><label><span>Message preset</span><select aria-label="Original message preset" defaultValue="" onChange={(event) => { const preset = presets.find((item) => item.name === event.target.value); if (preset) applyPreset(preset); event.target.value = '' }}><option value="">Choose a preset...</option>{presets.map((preset) => <option key={preset.name} value={preset.name}>{preset.name}</option>)}</select></label><button className="button compact" onClick={() => setTab('presets')}>Manage presets</button></div>
              <label className="wide"><span>HTML body override</span><textarea value={originalBody} onChange={(event) => setOriginalBody(event.target.value)} rows={8} placeholder="Optional. Leave blank to use recipient, organization, or default HTML templates." /></label>
              <label><span>Subject override</span><input value={subject} onChange={(event) => setSubject(event.target.value)} placeholder="Use CSV or campaign default" /></label>
              <label><span>Common attachments</span><div className="attachment-picker">{attachmentFiles.map((file) => <button type="button" key={file.fileName} className={attachmentList(attachments).includes(file.fileName) ? 'selected' : ''} onClick={() => void toggleCampaignAttachment(file.fileName)} disabled={busy !== null}><Paperclip size={13} />{file.fileName}</button>)}{!attachmentFiles.length && <small>Upload a file to select it.</small>}</div></label>
            </div>
            <label className="check-row"><input type="checkbox" checked={allowResend} onChange={(event) => setAllowResend(event.target.checked)} /><span>Allow selected recipients that were previously sent to</span></label>
            {selectedEmails.size > 0 && <div className="recipient-warnings"><strong>Review warnings</strong><span>These are warnings only; preview and sending remain available.</span><div className="warning-chips">{recipientWarnings.duplicateEmails > 0 && <span>Duplicate emails: {recipientWarnings.duplicateEmails}</span>}{recipientWarnings.crossCampaign > 0 && <span>Seen in another campaign: {recipientWarnings.crossCampaign}</span>}{recipientWarnings.recentlySent > 0 && <span>Sent within 7 days: {recipientWarnings.recentlySent}</span>}{recipientWarnings.replied > 0 && <span>Already replied: {recipientWarnings.replied}</span>}{recipientWarnings.bounced > 0 && <span>Bounced: {recipientWarnings.bounced}</span>}{recipientWarnings.repeatedOrganizations > 0 && <span>Repeated organizations: {recipientWarnings.repeatedOrganizations}</span>}{!Object.values(recipientWarnings).some((count) => count > 0) && <span className="warning-clear">No duplicate or recent-contact warnings.</span>}</div></div>}
            <div className="table-tools"><div className="search"><Search size={15} /><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search recipients" /></div><div className="selection-tools"><span>{selectedEmails.size} selected</span><button className={`button compact${showSelectedOnly ? ' active' : ''}`} aria-pressed={showSelectedOnly} onClick={() => setShowSelectedOnly((current) => !current)} disabled={!selectedEmails.size && !showSelectedOnly}>Selected only</button><button className="button compact" onClick={() => setSelectedEmails((current) => new Set([...current, ...filteredRecipients.filter((recipient) => !recipient.originalMessage).map((recipient) => recipient.email)]))} disabled={!filteredRecipients.some((recipient) => !recipient.originalMessage && !selectedEmails.has(recipient.email))}>Select uncontacted</button><select className="compact-select" aria-label="Select first visible" defaultValue="" onChange={(event) => { if (event.target.value) selectFirstVisible(Number(event.target.value)); event.target.value = '' }} disabled={!filteredRecipients.length}><option value="">Select first...</option><option value="25">First 25</option><option value="50">First 50</option><option value="100">First 100</option></select><button className="button compact" onClick={invertVisibleSelection} disabled={!filteredRecipients.length}>Invert visible</button><button className="button compact" onClick={() => setSelectedEmails((current) => new Set([...current, ...filteredRecipients.map((recipient) => recipient.email)]))} disabled={!filteredRecipients.length}>Select all visible</button><button className="button compact" onClick={() => setSelectedEmails(new Set())} disabled={!selectedEmails.size}>Clear selection</button></div></div>
            <div className="table-wrap"><table><thead><tr><th className="check-cell"><input type="checkbox" checked={filteredRecipients.length > 0 && filteredRecipients.every((recipient) => selectedEmails.has(recipient.email))} onChange={(event) => setSelectedEmails(event.target.checked ? new Set(filteredRecipients.map((recipient) => recipient.email)) : new Set())} /></th><th>Recipient</th><th>Organization</th><th>Subject</th><th>Attachments</th><th>Status</th><th className="actions-cell">Actions</th></tr></thead><tbody>
              {filteredRecipients.map((recipient) => <tr key={recipient.id}><td className="check-cell"><input type="checkbox" checked={selectedEmails.has(recipient.email)} onChange={() => toggleEmail(recipient.email, selectedEmails, setSelectedEmails)} /></td><td><strong>{recipient.name}</strong><small>{recipient.email}</small></td><td>{recipient.organization}</td><td>{recipient.subject || 'Campaign default'}</td><td>{recipient.attachments.length ? <span className="attachment-names">{recipient.attachments.join(', ')}</span> : <span className="muted-value">None</span>}</td><td><span className={`status ${recipient.originalMessage ? 'warning' : 'neutral'}`}>{recipient.originalMessage ? 'Previously sent' : 'Ready'}</span></td><td className="actions-cell"><button className="row-action" title="Edit recipient" onClick={() => openEditRecipient(recipient)}><Pencil size={14} /></button><button className="row-action destructive" title="Delete recipient" onClick={() => void deleteRecipient(recipient)}><Trash2 size={14} /></button></td></tr>)}
              {!filteredRecipients.length && <tr><td colSpan={7} className="empty-row">Add a recipient or import CSV to begin.</td></tr>}
            </tbody></table></div>
            <div className="action-bar"><button className="button" onClick={previewOriginal} disabled={!selectedEmails.size || busy !== null}><Inbox size={16} /> Preview batch</button>{originalPreview && <><div className="preview-summary"><strong>{originalPreview.eligibleRecipients}</strong> eligible · {originalPreview.alreadySentRecipients} previously sent · {originalPreview.remainingToday} remaining today</div><input className="confirm-input" value={originalConfirmation} onChange={(event) => setOriginalConfirmation(event.target.value)} placeholder="Type SEND ORIGINAL EMAILS" /><button className="button danger" onClick={sendOriginals} disabled={originalConfirmation !== 'SEND ORIGINAL EMAILS' || !selectedCampaign || busy !== null}><Send size={16} /> Send originals</button></>}</div>
            {originalReview && <div className="pre-send-review"><div className="pre-send-review-heading"><div><strong>Pre-send review</strong><span>Read-only checks for this preview. Sending remains disabled until the exact confirmation is entered.</span></div><span className={`status ${originalReview.blocked ? 'warning' : 'success'}`}>{originalReview.blocked ? `${originalReview.blocked} blocked` : 'Ready to review'}</span></div><div className="review-grid"><div><span>Selected</span><strong>{originalReview.selected}</strong></div><div><span>Eligible</span><strong>{originalReview.eligible}</strong></div><div><span>Previously sent</span><strong>{originalReview.previouslySent}</strong></div><div><span>Invalid email</span><strong>{originalReview.invalid}</strong></div><div><span>Missing attachments</span><strong>{originalReview.missingAttachments}</strong></div><div><span>Duplicates</span><strong>{originalReview.duplicates}</strong></div><div><span>Replied selected</span><strong>{originalReview.replied}</strong></div><div><span>Remaining today</span><strong>{originalReview.remainingToday}</strong></div><div><span>Gmail authorization</span><strong>{gmail?.status === 'reauthorization-required' ? 'Expired' : gmail?.tokenStored ? `Valid until ${formatDate(gmail.authorizationExpiresAt)}` : 'Not connected'}</strong></div></div></div>}
            {originalPreview && <div className="preview-list">{originalPreview.recipients.map((recipient) => <div key={recipient.email} className="preview-line"><span className={`status ${recipient.eligible ? 'success' : 'warning'}`}>{recipient.eligible ? 'Eligible' : 'Blocked'}</span><div><strong>{recipient.email}</strong><small>{recipient.subject} · {recipient.bodySource}</small><small>Attachments: {recipient.attachments.length ? recipient.attachments.map((attachment) => attachment.fileName).join(', ') : 'None'}{recipient.errors.length ? ` · ${recipient.errors.join(' ')}` : ''}</small></div><button className="row-action" title="Preview rendered HTML" onClick={() => setOriginalBodyPreview(recipient)}><Eye size={14} /></button></div>)}</div>}
            {originalBodyPreview && <div className="reply-viewer"><div className="reply-viewer-header"><div><strong>{originalBodyPreview.subject}</strong><span>{originalBodyPreview.email} · {originalBodyPreview.bodySource}</span></div><button className="icon-button" title="Close preview" onClick={() => setOriginalBodyPreview(null)}><X size={16} /></button></div><iframe title="Rendered original email" sandbox="" srcDoc={originalBodyPreview.renderedBody} /></div>}
          </section>
        )}

        {tab === 'replies' && (
          <section className="panel">
            <div className="panel-heading"><div><h2>Reply status</h2><p>Read-only Gmail check. This action never sends email.</p></div><button className="button primary" onClick={checkReplies} disabled={!selectedCampaign || busy !== null}><RefreshCw size={16} className={busy === 'check-replies' ? 'spin' : ''} /> Check replies</button></div>
            <div className="filter-bar reply-filter"><label><span>Classification</span><select aria-label="Reply classification" value={replyClassificationFilter} onChange={(event) => setReplyClassificationFilter(event.target.value as ReplyClassificationFilter)}><option value="all">All classifications</option><option value="replied">Replied</option><option value="no-reply">Awaiting reply</option><option value="bounced">Bounced</option><option value="automated">Automated</option><option value="uncertain">Uncertain</option><option value="missing-thread">Missing thread</option><option value="not-checked">Not checked</option></select></label><div className="filter-result"><strong>{visibleReplyRecipients.length}</strong> shown</div></div>
            <div className="table-wrap"><table><thead><tr><th>Recipient</th><th>Original sent</th><th>Last checked</th><th>Classification</th><th>Notes</th><th className="actions-cell">Reply</th></tr></thead><tbody>
              {visibleReplyRecipients.map((recipient) => <tr key={recipient.email}><td><strong>{recipient.name}</strong><small>{recipient.email}</small></td><td>{formatDate(recipient.originalMessage?.sentAt)}</td><td>{formatDate(recipient.replyCheck.checkedAt)}</td><td><span className={`status ${recipient.replyCheck.status}`}>{statusLabel(recipient.replyCheck.status)}</span></td><td>{recipient.replyCheck.notes ?? '—'}</td><td className="actions-cell">{recipient.replyCheck.matchedMessageId && <button className="row-action" title="Open matched reply" onClick={() => void openReply(recipient)} disabled={busy !== null}><Eye size={14} /></button>}</td></tr>)}
              {!visibleReplyRecipients.length && <tr><td colSpan={6} className="empty-row">No recipients match this classification.</td></tr>}
            </tbody></table></div>
            {replyView && <div className="reply-viewer"><div className="reply-viewer-header"><div><strong>{replyView.subject || '(No subject)'}</strong><span>{replyView.from} · {formatDate(replyView.receivedAt)}</span></div><button className="icon-button" title="Close reply" onClick={() => setReplyView(null)}><X size={16} /></button></div><dl><div><dt>From</dt><dd>{replyView.from}</dd></div><div><dt>To</dt><dd>{replyView.to || '—'}</dd></div></dl>{replyView.attachments.length > 0 && <div className="reply-attachments">{replyView.attachments.map((attachment) => <span key={attachment.fileName}><Paperclip size={13} /> {attachment.fileName} · {formatBytes(attachment.size)}</span>)}</div>}{replyView.sanitizedHtmlBody ? <iframe title="Reply content" sandbox="" srcDoc={replyView.sanitizedHtmlBody} /> : <pre>{replyView.plainTextBody || 'This reply has no readable body.'}</pre>}</div>}
          </section>
        )}

        {tab === 'followups' && (
          <section className="panel">
            <div className="panel-heading"><div><h2>Follow-up batch</h2><p>Only verified no-reply threads can be selected.</p></div><button className="button" onClick={() => previewFollowUps(true)} disabled={!selectedCampaign || busy !== null}><Inbox size={16} /> Preview candidates</button></div>
            <div className="form-grid follow-form"><div className="preset-bar wide"><label><span>Message preset</span><select aria-label="Follow-up message preset" defaultValue="" onChange={(event) => { const preset = presets.find((item) => item.name === event.target.value); if (preset) applyPreset(preset); event.target.value = '' }}><option value="">Choose a preset...</option>{presets.map((preset) => <option key={preset.name} value={preset.name}>{preset.name}</option>)}</select></label></div><label className="wide"><span>HTML body</span><textarea value={followBody} onChange={(event) => setFollowBody(event.target.value)} rows={7} /></label><label><span>Subject override</span><input value={followSubject} onChange={(event) => setFollowSubject(event.target.value)} placeholder="Re: original subject" /></label><label><span>Follow-up attachments</span><div className="attachment-picker">{attachmentFiles.map((file) => <button type="button" key={file.fileName} className={attachmentList(followAttachments).includes(file.fileName) ? 'selected' : ''} onClick={() => toggleAttachment(file.fileName, followAttachments, setFollowAttachments)}><Paperclip size={13} />{file.fileName}</button>)}{!attachmentFiles.length && <small>Upload attachments from the Original email tab.</small>}</div></label></div>
            <label className="check-row"><input type="checkbox" checked={allowSubjectChange} onChange={(event) => setAllowSubjectChange(event.target.checked)} /><span>Allow subject changes that may break Gmail threading</span></label>
            {followPreview && <div className="follow-status-tools"><label><span>Reply status</span><select value={followReplyFilter} onChange={(event) => setFollowReplyFilter(event.target.value as FollowReplyFilter)}><option value="eligible">Eligible / no reply</option><option value="all">All statuses</option><option value="no-reply">No reply</option><option value="replied">Replied</option><option value="automated">Automated</option><option value="bounced">Bounced</option><option value="uncertain">Uncertain</option><option value="missing-thread">Missing thread</option><option value="not-checked">Not checked</option></select></label><div className="selection-tools"><span>{followSelection.size} selected</span><button className="button compact" onClick={() => setFollowSelection((current) => new Set([...current, ...visibleFollowRecipients.filter((recipient) => recipient.eligible).map((recipient) => recipient.email)]))} disabled={!visibleFollowRecipients.some((recipient) => recipient.eligible)}>Select all visible</button><button className="button compact" onClick={() => { const visible = new Set(visibleFollowRecipients.map((recipient) => recipient.email)); setFollowSelection((current) => new Set([...current].filter((email) => !visible.has(email)))) }} disabled={!visibleFollowRecipients.some((recipient) => followSelection.has(recipient.email))}>Deselect all visible</button></div></div>}
            {followPreview && <div className="preview-list">{visibleFollowRecipients.map((recipient) => <div className="preview-line" key={`follow-attachments-${recipient.email}`}><div><strong>{recipient.email}</strong><small>Attachments: {recipient.attachments.length ? recipient.attachments.map((attachment) => attachment.fileName).join(', ') : 'None'}{recipient.attachments.some((attachment) => !attachment.exists) ? ' · Missing file' : ''}</small></div></div>)}</div>}
            {followBodyPreview && <div className="reply-viewer"><div className="reply-viewer-header"><div><strong>{followBodyPreview.subject}</strong><span>{followBodyPreview.email} · Follow-up preview</span></div><button className="icon-button" title="Close preview" onClick={() => setFollowBodyPreview(null)}><X size={16} /></button></div><iframe title="Rendered follow-up email" sandbox="" srcDoc={followBodyPreview.renderedBody} /></div>}
            {followPreview ? <><div className="filter-bar"><label><span>Show older than</span><div className="number-control"><input type="number" min="0" step="1" value={followOlderDays} onChange={(event) => setFollowOlderDays(Math.max(0, Number(event.target.value)))} /><span>days</span></div><div className="age-shortcuts" aria-label="Follow-up age shortcuts">{[2, 3, 5, 7, 14].map((days) => <button type="button" key={days} className={`button compact${followOlderDays === days ? ' active' : ''}`} aria-pressed={followOlderDays === days} onClick={() => setFollowOlderDays(days)}>{days}d</button>)}</div></label><label><span>Organization</span><select value={followOrganization} onChange={(event) => setFollowOrganization(event.target.value)}><option value="all">All organizations</option>{followOrganizations.map((organization) => <option key={organization} value={organization}>{organization}</option>)}</select></label><div className="filter-result"><strong>{visibleFollowRecipients.length}</strong> shown<button className="button compact" onClick={() => setFollowSelection(new Set(visibleFollowRecipients.filter((recipient) => recipient.eligible).map((recipient) => recipient.email)))}>Select visible</button></div></div><div className="table-wrap"><table><thead><tr><th className="check-cell"></th><th>Recipient</th><th>Organization</th><th>Waiting</th><th>Reply status</th><th>Follow-up subject</th><th>Eligibility</th><th className="actions-cell">Preview</th></tr></thead><tbody>{visibleFollowRecipients.map((recipient) => <tr key={recipient.email}><td className="check-cell"><input type="checkbox" disabled={!recipient.eligible} checked={followSelection.has(recipient.email)} onChange={() => toggleEmail(recipient.email, followSelection, setFollowSelection)} /></td><td><strong>{recipient.name}</strong><small>{recipient.email}</small></td><td>{recipient.organization || '—'}</td><td><strong>{formatElapsed(recipient.elapsedHours)}</strong><small>Since {formatDate(recipient.lastOutboundAt)}</small></td><td><span className={`status ${recipient.replyStatus}`}>{statusLabel(recipient.replyStatus)}</span></td><td>{recipient.subject}</td><td>{recipient.eligible ? <span className="status success">Eligible</span> : <span className="status warning">{recipient.errors[0] ?? 'Blocked'}</span>}</td><td className="actions-cell"><button className="row-action" title="Preview rendered follow-up" aria-label={`Preview follow-up for ${recipient.email}`} onClick={() => setFollowBodyPreview(recipient)}><Eye size={14} /></button></td></tr>)}{!visibleFollowRecipients.length && <tr><td colSpan={8} className="empty-row">No candidates match these filters.</td></tr>}</tbody></table></div><div className="action-bar"><button className="button" onClick={previewFollowUps} disabled={!followSelection.size || busy !== null}><Inbox size={16} /> Preview batch</button><div className="preview-summary"><strong>{followSelection.size}</strong> selected from {visibleFollowRecipients.filter((recipient) => recipient.eligible).length} visible eligible</div><input className="confirm-input" value={followConfirmation} onChange={(event) => setFollowConfirmation(event.target.value)} placeholder="Type SEND FOLLOW-UP EMAILS" /><button className="button danger" onClick={sendFollowUps} disabled={followConfirmation !== 'SEND FOLLOW-UP EMAILS' || !followSelection.size || busy !== null}><Send size={16} /> Send follow-ups</button></div></> : <div className="empty-state"><Users size={28} /><strong>No preview yet</strong><span>Run a reply check, then preview follow-up candidates.</span></div>}
          </section>
        )}

        {tab === 'settings' && settingsDraft && (
          <section className="panel settings-panel">
            <div className="panel-heading"><div><h2>Local settings</h2><p>Defaults for newly created campaigns and future outgoing messages.</p></div><button className="button primary" onClick={() => void saveSettings()} disabled={busy !== null || JSON.stringify(settingsDraft) === JSON.stringify(settings)}><Check size={16} /> Save settings</button></div>
            <div className="settings-grid">
              <label><span>Sender display name</span><input value={settingsDraft.senderName} onChange={(event) => setSettingsDraft({ ...settingsDraft, senderName: event.target.value })} /></label>
              <label><span>Daily send limit</span><input type="number" min="1" max="2000" value={settingsDraft.dailyLimit} onChange={(event) => setSettingsDraft({ ...settingsDraft, dailyLimit: Number(event.target.value) })} /></label>
              <label className="wide"><span>Default original subject</span><input value={settingsDraft.defaultSubject} onChange={(event) => setSettingsDraft({ ...settingsDraft, defaultSubject: event.target.value })} /></label>
              <label><span>Default template name</span><input value={settingsDraft.defaultTemplate} onChange={(event) => setSettingsDraft({ ...settingsDraft, defaultTemplate: event.target.value })} /></label>
              <label><span>Connected Gmail account</span><input value={settingsDraft.gmailAccount} readOnly /></label>
            </div>
            <div className="settings-note"><CircleAlert size={16} /><span>Existing campaign defaults are unchanged. OAuth credentials and local storage paths remain managed in the ignored appsettings.json file.</span></div>
            <div className="settings-note"><CircleAlert size={16} /><span>Delivery counts: last 24 hours <strong>{settingsDraft.sentLast24Hours ?? 'Unavailable'}</strong> · today (local) <strong>{settingsDraft.sentTodayLocal ?? 'Unavailable'}</strong> · today (UTC) <strong>{settingsDraft.sentTodayUtc ?? 'Unavailable'}</strong>. {settingsDraft.sentLast24Hours == null && 'Restart the localhost backend to load delivery counts.'}</span></div>
          </section>
        )}

        {tab === 'settings' && settingsDraft && <section className="panel"><div className="panel-heading"><div><h2>Workspace exports</h2><p>Select campaigns to export consolidated files.</p></div><span className="status neutral">{exportCampaignIds.size} selected</span></div><div className="export-picker"><div className="table-tools"><div className="search"><Search size={15}/><input value={exportCampaignSearch} onChange={event => setExportCampaignSearch(event.target.value)} placeholder="Search campaigns" /></div><div className="selection-tools"><button className="button compact" onClick={() => setExportCampaignIds(current => new Set([...current, ...exportCampaignOptions.map(c => c.id)]))} disabled={!exportCampaignOptions.length}>Select all matching</button><button className="button compact" onClick={() => setExportCampaignIds(new Set())} disabled={!exportCampaignIds.size}>Clear selection</button></div></div><div className="campaign-export-list">{exportCampaignOptions.map(c => <label key={c.id}><input type="checkbox" checked={exportCampaignIds.has(c.id)} onChange={() => setExportCampaignIds(current => { const n=new Set(current); if (n.has(c.id)) n.delete(c.id); else n.add(c.id); return n })}/><span><strong>{c.name}</strong><small>{c.recipients.length} recipient{c.recipients.length === 1 ? '' : 's'}</small></span></label>)}{!exportCampaignOptions.length && <div className="empty-row">No campaigns match this search.</div>}</div></div><div className="action-bar"><button className="button" disabled={!exportCampaignIds.size} onClick={exportWorkspaceMarkdown}><Download size={16}/> Markdown</button><button className="button" disabled={!exportCampaignIds.size} onClick={() => exportWorkspaceCsv(false)}><Download size={16}/> Full CSV</button><button className="button primary" disabled={!exportCampaignIds.size} onClick={() => exportWorkspaceCsv(true)}><Download size={16}/> Valid contacts CSV</button></div></section>}

        {tab === 'activity' && (
          <section className="panel">
            <div className="panel-heading"><div><h2>Campaign activity</h2><p>Locally stored Gmail delivery and follow-up history.</p></div></div>
            <div className="activity-list">{(selectedCampaign?.recipients ?? []).flatMap((recipient) => [
              ...(recipient.originalMessage ? [{ type: 'Original', date: recipient.originalMessage.sentAt, subject: recipient.originalMessage.subject, email: recipient.email, id: recipient.originalMessage.providerMessageId }] : []),
              ...recipient.followUps.map((message) => ({ type: 'Follow-up', date: message.sentAt, subject: message.subject, email: recipient.email, id: message.providerMessageId })),
            ]).sort((a, b) => +new Date(b.date) - +new Date(a.date)).map((item, index) => <div className="activity-row" key={`${item.id}-${index}`}><div className="activity-icon"><Mail size={16} /></div><div><strong>{item.type} sent to {item.email}</strong><span>{item.subject}</span></div><time>{formatDate(item.date)}</time></div>)}{!selectedCampaign?.recipients.some((recipient) => recipient.originalMessage) && <div className="empty-state"><Mail size={28} /><strong>No activity yet</strong><span>Sent messages will appear here.</span></div>}</div>
          </section>
        )}

        {tab === 'presets' && <section className="panel preset-manager"><div className="panel-heading"><div><h2>Message presets</h2><p>Reusable original and follow-up content stored locally in this browser.</p></div><button className="button primary" onClick={newPreset}><Plus size={16} /> New preset</button></div><div className="preset-manager-layout"><div className="preset-list">{presets.map((preset) => <div className={editingPresetName === preset.name ? 'preset-item active' : 'preset-item'} key={preset.name}><div><strong>{preset.name}</strong><small>{preset.subject || 'No subject override'} · {preset.attachments || 'No common attachments'}</small></div><div className="preset-item-actions"><button className="row-action" title="Edit preset" onClick={() => editPreset(preset)}><Pencil size={14} /></button><button className="row-action destructive" title="Delete preset" onClick={() => deletePreset(preset.name)}><Trash2 size={14} /></button></div></div>)}{!presets.length && <div className="empty-state"><Copy size={28} /><strong>No presets yet</strong><span>Create a reusable message preset.</span></div>}</div><div className="preset-editor"><h3>{editingPresetName ? 'Edit preset' : 'Create preset'}</h3><label><span>Name</span><input value={presetDraft.name} onChange={(event) => setPresetDraft({ ...presetDraft, name: event.target.value })} placeholder="Preset name" /></label><label><span>Original subject</span><input value={presetDraft.subject} onChange={(event) => setPresetDraft({ ...presetDraft, subject: event.target.value })} /></label><label><span>Original HTML body</span><textarea value={presetDraft.body} onChange={(event) => setPresetDraft({ ...presetDraft, body: event.target.value })} rows={5} /></label><label><span>Common attachments</span><input value={presetDraft.attachments} onChange={(event) => setPresetDraft({ ...presetDraft, attachments: event.target.value })} placeholder="resume.pdf, cover-letter.pdf" /></label><label><span>Follow-up subject</span><input value={presetDraft.followSubject} onChange={(event) => setPresetDraft({ ...presetDraft, followSubject: event.target.value })} /></label><label><span>Follow-up HTML body</span><textarea value={presetDraft.followBody} onChange={(event) => setPresetDraft({ ...presetDraft, followBody: event.target.value })} rows={4} /></label><label><span>Follow-up attachments</span><input value={presetDraft.followAttachments} onChange={(event) => setPresetDraft({ ...presetDraft, followAttachments: event.target.value })} placeholder="resume.pdf" /></label><div className="editor-actions"><button className="button" onClick={newPreset}>Clear</button><button className="button primary" onClick={savePreset} disabled={!presetDraft.name.trim()}><Check size={16} /> Save preset</button></div></div></div></section>}

        {tab === 'summary' && (
          <section className="panel campaign-report">
            <div className="panel-heading"><div><h2>Campaign summary</h2><p>Read-only report generated from locally stored campaign state.</p></div><div className="heading-actions"><button className="button" onClick={exportMarkdownReport}><Download size={16} /> Markdown</button><button className="button" onClick={exportCampaignCsv}><Download size={16} /> Full CSV</button><button className="button primary" onClick={exportValidContactsCsv}><Download size={16} /> Valid contacts CSV</button></div></div>
            <div className="report-metrics"><div><span>Recipients</span><strong>{campaignReport.total}</strong></div><div><span>Companies</span><strong>{campaignReport.companies.length}</strong></div><div><span>Sent</span><strong>{campaignReport.sent}</strong></div><div><span>Not sent</span><strong>{campaignReport.unsent}</strong></div><div><span>Replies</span><strong>{campaignReport.replied}</strong></div><div><span>Awaiting</span><strong>{campaignReport.awaiting}</strong></div><div><span>Bounced</span><strong>{campaignReport.bounced}</strong></div><div><span>Follow-ups</span><strong>{campaignReport.followUps}</strong></div><div><span>Valid contacts</span><strong>{campaignReport.validContacts.length}</strong></div></div>
            <div className="report-note"><CircleAlert size={16} /><span>Valid contacts are deduplicated by email and exclude invalid email syntax and bounced recipients. Exports never query Gmail or include OAuth credentials.</span></div>
            <div className="table-wrap"><table><thead><tr><th>Company</th><th>Contacts</th><th>Sent</th><th>Replied</th><th>Awaiting</th><th>Bounced</th></tr></thead><tbody>{campaignReport.companies.map((company) => <tr key={company.name.toLowerCase()}><td><strong>{company.name}</strong></td><td>{company.contacts}</td><td>{company.sent}</td><td>{company.replied}</td><td>{company.awaiting}</td><td>{company.bounced}</td></tr>)}{!campaignReport.companies.length && <tr><td colSpan={6} className="empty-row">No campaign recipients to summarize.</td></tr>}</tbody></table></div>
          </section>
        )}

        {tab === 'help' && <section className="panel guide"><div className="panel-heading"><div><h2>Help & Guide</h2><p>Updated {guideUpdated} · Local application reference</p></div><div className="search guide-search"><Search size={15} /><input value={guideSearch} onChange={(event) => setGuideSearch(event.target.value)} placeholder="Search the guide" /></div></div><div className="guide-layout"><div className="guide-sections">{visibleGuideSections.map((section) => <section key={section.title}><h3>{section.title}</h3><ul>{section.items.map((item) => <li key={item}>{item}</li>)}</ul></section>)}{!visibleGuideSections.length && <div className="empty-state"><Search size={24} /><strong>No matching guide section</strong></div>}</div><aside className="placeholder-reference"><h3>Placeholder reference</h3>{placeholders.map((placeholder) => <div key={placeholder.syntax}><code>{placeholder.syntax}</code><strong>{placeholder.value}</strong><span>{placeholder.example}</span></div>)}</aside></div></section>}
      </main>
    </div>
  )
}

export default App

