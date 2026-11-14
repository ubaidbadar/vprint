import {
  memo, useCallback, useDeferredValue, useEffect, useMemo, useRef, useState,
  type FormEvent, type MouseEvent, type ReactNode,
} from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import {
  AlertTriangle, ArrowRight, Check, CircleHelp, Clock3, FileText, Inbox,
  Info, LoaderCircle, Plus, Printer, RefreshCw, Search, ShieldCheck, Trash2, X,
} from 'lucide-react';
import { api, errorMessage } from './api';
import type { Printer as Queue, PrintJob, Snapshot } from './types';

const EMPTY: Snapshot = {
  printers: [], jobs: [], totalCount: 0, todayCount: 0,
  historyEnabled: false, historyWarning: null,
};
const PREFIX = 'VPrint - ';

function formatDate(value: string) {
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? 'Unknown' :
    new Intl.DateTimeFormat(undefined, {
      month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
    }).format(d);
}
function formatBytes(size: number) {
  if (size < 1) return '—';
  if (size < 1024) return size + ' B';
  return size < 1048576 ? (size / 1024).toFixed(1) + ' KB' :
    (size / 1048576).toFixed(1) + ' MB';
}

const NavQueue = memo(function NavQueue({ queue, active, count, onSelect }: {
  queue: Queue; active: boolean; count: number; onSelect: (name: string) => void;
}) {
  return <button className={'nav-row ' + (active ? 'active' : '')} onClick={() => onSelect(queue.name)}
    aria-pressed={active} title={queue.name} type="button">
    <Printer size={16} aria-hidden="true" />
    <span className="ellipsis">{queue.label}</span><span className="nav-count">{count}</span>
  </button>;
});

const HistoryRow = memo(function HistoryRow({ job, y, onSelect }: {
  job: PrintJob; y: number; onSelect: (job: PrintJob) => void;
}) {
  return <button className="job-row" onClick={() => onSelect(job)}
    style={{ transform: 'translateY(' + y + 'px)' }} type="button"
    aria-label={'View ' + job.documentName + ' details'}>
    <span className="job-title ellipsis"><span className="file-icon"><FileText size={16} /></span>
      <span className="ellipsis">{job.documentName}</span></span>
    <span className="ellipsis muted">{job.printerName.startsWith(PREFIX) ?
      job.printerName.slice(PREFIX.length) : job.printerName}</span>
    <span className="ellipsis muted">{job.userName}</span>
    <span className="muted end">{job.pages || '—'}</span>
    <span className="muted end">{formatDate(job.printedAt)}</span>
  </button>;
});

function Dialog({ title, children, onClose }: {
  title: string; children: ReactNode; onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const closeRef = useRef(onClose);
  useEffect(() => { closeRef.current = onClose; }, [onClose]);
  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement ?
      document.activeElement : null;
    ref.current?.focus();
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); closeRef.current(); }
      if (event.key !== 'Tab' || !ref.current) return;
      const elements = [...ref.current.querySelectorAll<HTMLElement>(
        'button:not(:disabled), input:not(:disabled), [tabindex]:not([tabindex="-1"])')];
      if (!elements.length) return;
      const first = elements[0], last = elements[elements.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault(); first.focus();
      }
    };
    document.addEventListener('keydown', key);
    return () => { document.removeEventListener('keydown', key); previouslyFocused?.focus(); };
  }, []);

  const backdrop = (event: MouseEvent<HTMLDivElement>) => {
    if (event.target === event.currentTarget) onClose();
  };
  return <div className="overlay" onMouseDown={backdrop}>
    <div className="dialog" role="dialog" aria-modal="true" aria-label={title}
      tabIndex={-1} ref={ref}>
      <div className="dialog-heading"><h2>{title}</h2>
        <button className="icon-button" type="button" onClick={onClose}
          aria-label="Close dialog"><X size={19} /></button></div>
      {children}
    </div>
  </div>;
}

function NewPrinterForm({ busy, onCreate, onCancel }: {
  busy: boolean; onCreate: (name: string) => Promise<void>; onCancel: () => void;
}) {
  const [name, setName] = useState('');
  const [touched, setTouched] = useState(false);
  const field = useRef<HTMLInputElement>(null);
  useEffect(() => { field.current?.focus(); }, []);
  const bad = !name.trim() || name.trim().length > 54 ||
    /[\\/:*?"<>|\x00-\x1F]/.test(name) || name.trim().endsWith('.');
  const submit = (event: FormEvent) => {
    event.preventDefault(); setTouched(true);
    if (!bad && !busy) void onCreate(name.trim());
  };
  return <form onSubmit={submit}>
    <p className="dialog-desc">Create a printer that appears in the normal Windows print dialog.</p>
    <label className="field-label" htmlFor="queue-name">Printer name</label>
    <input ref={field} id="queue-name" className="form-input" maxLength={54}
      placeholder="e.g. Kitchen, Cashier or Receipts" value={name}
      onChange={event => setName(event.currentTarget.value)}
      aria-invalid={touched && bad} aria-describedby="printer-hint" />
    <p id="printer-hint" className={'hint ' + (touched && bad ? 'error-text' : '')}>
      {touched && bad ? 'Enter a name without Windows reserved characters.' :
      'Windows printer: VPrint - ' + (name.trim() || 'Kitchen')}
    </p>
    <div className="subtle-notice"><ShieldCheck size={17} />
      <span>Windows may request administrator approval. PowerShell is never launched.</span></div>
    <div className="dialog-actions">
      <button className="button outline" type="button" disabled={busy} onClick={onCancel}>Cancel</button>
      <button className="button primary" type="submit" disabled={bad || busy}>
        {busy ? <LoaderCircle size={17} className="spin" /> : <Plus size={17} />}
        {busy ? 'Creating…' : 'Create printer'}</button>
    </div>
  </form>;
}

function HelpContent() {
  return <div className="help">
    <p><strong>1. Create a printer.</strong> Add a named queue such as Kitchen or Receipts.</p>
    <p><strong>2. Print from Windows.</strong> Select VPrint - Kitchen in your app's print dialog.
      Windows Print to PDF still asks where to save the document.</p>
    <p><strong>3. View history.</strong> VPrint records job metadata from Windows PrintService.</p>
    <div className="subtle-notice"><Info size={17} /><span>
      To enable recording once: open Windows Event Viewer → Applications and Services Logs
      → Microsoft → Windows → PrintService → Operational, then choose <strong>Enable Log</strong>.
      Windows might ask for administrator permission.
    </span></div>
  </div>;
}

export default function App() {
  const [data, setData] = useState<Snapshot>(EMPTY);
  const [initialLoading, setInitialLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [enablingHistory, setEnablingHistory] = useState(false);
  const [busy, setBusy] = useState(false);
  const [activeQueue, setActiveQueue] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const deferredQuery = useDeferredValue(query.trim().toLowerCase());
  const [createOpen, setCreateOpen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  const [deleteQueue, setDeleteQueue] = useState<Queue | null>(null);
  const [details, setDetails] = useState<PrintJob | null>(null);
  const [notification, setNotification] = useState<{type: 'error' | 'success'; message: string} | null>(null);
  const inFlight = useRef(false);
  const mounted = useRef(true);
  const lastValue = useRef('');
  const scroll = useRef<HTMLDivElement>(null);

  const refresh = useCallback(async (manual = false) => {
    if (inFlight.current) return;
    inFlight.current = true;
    if (manual) setSyncing(true);
    try {
      const value = await api.getSnapshot();
      if (mounted.current) {
        const serialized = JSON.stringify(value);
        if (lastValue.current !== serialized) {
          lastValue.current = serialized;
          setData(value);
        }
      }
    } catch (err) {
      if (mounted.current) setNotification({ type: 'error', message: errorMessage(err) });
    } finally {
      inFlight.current = false;
      if (mounted.current) {
        setInitialLoading(false);
        setSyncing(false);
      }
    }
  }, []);

  useEffect(() => {
    mounted.current = true;
    void refresh();
    const timer = window.setInterval(() => { if (!document.hidden) void refresh(); }, 6000);
    const visibility = () => { if (!document.hidden) void refresh(); };
    document.addEventListener('visibilitychange', visibility);
    return () => {
      mounted.current = false;
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', visibility);
    };
  }, [refresh]);

  useEffect(() => {
    if (!notification) return;
    const timeout = window.setTimeout(() => setNotification(null), 6500);
    return () => window.clearTimeout(timeout);
  }, [notification]);

  const selectQueue = useCallback((name: string | null) => {
    setActiveQueue(name);
    scroll.current?.scrollTo({ top: 0 });
  }, []);

  const queueCounts = useMemo(() => {
    const map = new Map<string, number>();
    data.jobs.forEach(job => map.set(job.printerName, 1 + (map.get(job.printerName) || 0)));
    return map;
  }, [data.jobs]);
  const queue = data.printers.find(printer => printer.name === activeQueue);
  const visible = useMemo(() => data.jobs.filter(job => {
    if (activeQueue && job.printerName !== activeQueue) return false;
    return !deferredQuery ||
      (job.documentName + ' ' + job.printerName + ' ' + job.userName)
        .toLowerCase().includes(deferredQuery);
  }), [data.jobs, activeQueue, deferredQuery]);

  const virtualizer = useVirtualizer({
    count: visible.length,
    getScrollElement: () => scroll.current,
    estimateSize: () => 60,
    getItemKey: index => visible[index]?.id || index,
    overscan: 7,
  });

  const enableHistory = async () => {
    if (enablingHistory) return;
    setEnablingHistory(true);
    try {
      await api.enableHistory();
      setNotification({ type: 'success', message: 'Windows print history is enabled.' });
      await refresh(true);
    } catch (err) {
      setNotification({ type: 'error', message: errorMessage(err) });
    } finally { setEnablingHistory(false); }
  };

  const create = async (name: string) => {
    if (busy) return;
    setBusy(true);
    try {
      await api.createPrinter(name);
      setCreateOpen(false);
      setNotification({ type: 'success', message: 'VPrint - ' + name + ' created.' });
      await refresh(true);
      selectQueue(PREFIX + name);
    } catch (err) {
      setNotification({ type: 'error', message: errorMessage(err) });
    } finally { setBusy(false); }
  };

  const remove = async () => {
    if (!deleteQueue || busy) return;
    setBusy(true);
    try {
      await api.deletePrinter(deleteQueue.name);
      if (activeQueue === deleteQueue.name) selectQueue(null);
      setNotification({ type: 'success', message: deleteQueue.label + ' removed.' });
      setDeleteQueue(null);
      await refresh(true);
    } catch (err) {
      setNotification({ type: 'error', message: errorMessage(err) });
    } finally { setBusy(false); }
  };

  const chooseDetails = useCallback((job: PrintJob) => setDetails(job), []);

  return <div className="app-shell">
    <aside className="sidebar">
      <div className="brand"><img src="/logo.svg" className="brand-logo" alt="VPrint printer logo" />
        <div><strong>VPrint</strong><small>PRINT WORKSPACE</small></div></div>
      <div className="nav-label">WORKSPACE</div>
      <button type="button" className={'nav-row all-row ' + (!activeQueue ? 'active' : '')}
        onClick={() => selectQueue(null)} aria-pressed={!activeQueue}>
        <Inbox size={17}/><span>All print jobs</span><span className="nav-count">{data.totalCount}</span></button>
      <div className="nav-heading"><span>YOUR PRINTERS</span><span>{data.printers.length}</span></div>
      <div className="nav-list" role="navigation" aria-label="Virtual printers">
        {data.printers.map(printer => <NavQueue key={printer.name} queue={printer}
          active={activeQueue === printer.name} count={queueCounts.get(printer.name) || 0}
          onSelect={selectQueue}/>)}
        {!data.printers.length && <div className="sidebar-empty">No printers yet.</div>}
      </div>
      <div className="sidebar-footer">
        <button type="button" className="nav-row help-link" onClick={() => setHelpOpen(true)}>
          <CircleHelp size={17}/> How it works <ArrowRight className="nav-right" size={15}/></button>
        <div className="private-label"><ShieldCheck size={20}/><span><strong>Private by design</strong>
          <small>Your history stays on this PC.</small></span></div>
        <div className="version">VPrint · Rust edition 0.2</div>
      </div>
    </aside>

    <main className="main">
      <header className="header">
        <div><span className="eyebrow">PRINTER MANAGEMENT</span><h1>Print overview</h1>
          <p>Everything printed, organized by destination.</p></div>
        <div className="header-actions">
          <button className="button outline" type="button" onClick={() => void refresh(true)} disabled={syncing}>
            <RefreshCw size={17} className={syncing ? 'spin' : ''}/> Refresh</button>
          <button className="button primary" type="button" onClick={() => setCreateOpen(true)}>
            <Plus size={17}/> Add printer</button></div>
      </header>

      {initialLoading ? <div className="initial-loading" aria-label="Loading dashboard">
        <div className="skeleton-grid"><div/><div/><div/></div><div className="skeleton-panel"/></div>
      : <>
        <section className="metrics" aria-label="Printer statistics">
          <div className="metric"><div><span>Virtual printers</span><Printer size={19}/></div>
            <strong>{data.printers.length}</strong><small>Available in Windows</small></div>
          <div className="metric"><div><span>Printed today</span><Clock3 size={19}/></div>
            <strong>{data.todayCount.toLocaleString()}</strong><small>Completed print jobs</small></div>
          <div className="metric"><div><span>Print history</span><FileText size={19}/></div>
            <strong>{data.totalCount.toLocaleString()}</strong><small>Stored locally</small></div>
        </section>

        {data.historyWarning && <div className="warning" role="status">
          <AlertTriangle size={19}/><span><strong>Print history needs attention</strong><br/>
            {data.historyWarning}</span>
          <button type="button" disabled={enablingHistory} onClick={() => void enableHistory()}>
            {enablingHistory ? <LoaderCircle size={14} className="spin"/> : <Check size={14}/>}
            {enablingHistory ? 'Enabling…' : 'Enable history'}</button>
        </div>}

        <section className="printers-section">
          <div className="section-heading"><div><h2>Your printers</h2>
            <p>Choose a queue to filter its jobs.</p></div>
            <button type="button" className="text-button" onClick={() => setCreateOpen(true)}>
              <Plus size={16}/> New printer</button></div>
          {!data.printers.length ?
            <div className="printer-empty"><div className="empty-icon"><Printer size={22}/></div>
              <div><strong>Create your first virtual printer</strong>
                <p>It will appear in the Windows print dialog.</p></div>
              <button className="button primary" onClick={() => setCreateOpen(true)} type="button">
                <Plus size={15}/> Create</button></div>
          : <div className="printer-grid">{data.printers.map(printer =>
            <button className={'printer-card ' + (activeQueue === printer.name ? 'selected' : '')}
              key={printer.name} type="button" onClick={() => selectQueue(printer.name)}
              aria-pressed={activeQueue === printer.name}>
              <div className="printer-card-top"><span className="printer-symbol"><Printer size={20}/></span>
                <span className="installed"><i/> Installed</span></div>
              <strong className="ellipsis">{printer.label}</strong>
              <div className="printer-card-end">{queueCounts.get(printer.name) || 0} recent jobs
                <ArrowRight size={15}/></div></button>)}</div>}
        </section>

        <section className="history-section">
          <div className="section-heading history-heading">
            <div><div className="history-title"><h2>{queue?.label || 'Print history'}</h2>
              <span>{visible.length} shown</span></div>
              <p>Successful print jobs reported by Windows.</p></div>
            <div className="history-actions">
              <label className="search-field"><Search size={17}/>
                <span className="sr-only">Search documents, printers and users</span>
                <input type="search" placeholder="Search print jobs…" value={query}
                  onChange={event => setQuery(event.currentTarget.value)}/></label>
              {queue && <button className="button danger-outline" type="button"
                onClick={() => setDeleteQueue(queue)}><Trash2 size={16}/> Remove</button>}
            </div>
          </div>
          <div className="history-panel">
            <div className="table-heading"><span>DOCUMENT</span><span>PRINTER</span>
              <span>USER</span><span className="end">PAGES</span><span className="end">PRINTED AT</span></div>
            <div className="history-scroll" ref={scroll}>
              {visible.length ?
              <div className="virtual-list" style={{height: virtualizer.getTotalSize()}}>
                {virtualizer.getVirtualItems().map(item => <HistoryRow key={item.key}
                  job={visible[item.index]} y={item.start} onSelect={chooseDetails}/>)}</div>
              : <div className="history-empty"><span className="empty-icon"><Inbox size={28}/></span>
                <strong>{deferredQuery ? 'No matching jobs' : 'No print jobs yet'}</strong>
                <p>{deferredQuery ? 'Try another search or select a different printer.' :
                  'Print to a VPrint printer to see completed jobs here.'}</p></div>}
            </div>
            <div className="history-bottom">
              <span><i className={data.historyEnabled ? 'dot' : 'dot inactive'}/>
                {data.historyEnabled ? 'Local history enabled' : 'Print history disabled'}</span>
              <span>Showing up to 500 recent jobs</span>
            </div>
          </div>
          <p className="footnote"><Info size={16}/>
            Current printers use Windows Print to PDF, which still prompts for a filename.
            Automatic PDF capture and document previews are a separate, upcoming feature.</p>
        </section>
      </>}
    </main>

    {notification && <div role={notification.type === 'error' ? 'alert' : 'status'}
      className={'toast ' + notification.type}>
      {notification.type === 'error' ? <AlertTriangle size={18}/> : <Check size={18}/>}
      <span>{notification.message}</span>
      <button aria-label="Dismiss notification" type="button"
        onClick={() => setNotification(null)}><X size={17}/></button></div>}

    {createOpen && <Dialog title="Add virtual printer" onClose={() => { if (!busy) setCreateOpen(false); }}>
      <NewPrinterForm busy={busy} onCreate={create} onCancel={() => setCreateOpen(false)}/>
    </Dialog>}
    {helpOpen && <Dialog title="How VPrint works" onClose={() => setHelpOpen(false)}>
      <HelpContent/><div className="dialog-actions"><button type="button" className="button primary"
        onClick={() => setHelpOpen(false)}>Got it <Check size={16}/></button></div>
    </Dialog>}
    {deleteQueue && <Dialog title="Remove virtual printer" onClose={() => { if (!busy) setDeleteQueue(null); }}>
      <p className="dialog-desc">Remove <strong>{deleteQueue.label}</strong> from Windows?
        Your existing print history will be kept.</p>
      <div className="dialog-actions">
        <button className="button outline" type="button" disabled={busy}
          onClick={() => setDeleteQueue(null)}>Cancel</button>
        <button className="button danger" type="button" disabled={busy} onClick={() => void remove()}>
          {busy ? <LoaderCircle size={17} className="spin"/> : <Trash2 size={17}/>}
          {busy ? 'Removing…' : 'Remove printer'}</button>
      </div>
    </Dialog>}
    {details && <Dialog title="Print job details" onClose={() => setDetails(null)}>
      <div className="details-heading"><FileText size={22}/><strong>{details.documentName}</strong></div>
      <dl className="details-list">
        <div><dt>Printer</dt><dd>{details.printerName}</dd></div>
        <div><dt>Windows user</dt><dd>{details.userName}</dd></div>
        <div><dt>Printed at</dt><dd>{formatDate(details.printedAt)}</dd></div>
        <div><dt>Pages</dt><dd>{details.pages || 'Not reported'}</dd></div>
        <div><dt>Job size</dt><dd>{formatBytes(details.sizeBytes)}</dd></div>
        <div><dt>Job number</dt><dd>{details.jobNumber || 'Not reported'}</dd></div>
      </dl>
      <div className="subtle-notice"><Info size={17}/>
        This records metadata only; document contents are not captured yet.</div>
      <div className="dialog-actions"><button className="button primary" type="button"
        onClick={() => setDetails(null)}>Close</button></div>
    </Dialog>}
  </div>;
}
