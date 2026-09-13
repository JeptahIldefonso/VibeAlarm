import type { TaskItem } from '../bridge/protocol';
import { Icon } from '../components/Icon';
import { taskDateTime, whenLabel } from '../lib/tasks';

/**
 * The Dashboard: greeting + live clock (host-pushed system time, never a local
 * counter), the two fixed-badge stat tiles (Active Tasks amber, Done Today
 * violet — metrics, never the accent), the NEXT UP countdown, and the
 * today/upcoming digests.
 */
export function DashboardView({ tasks, now, nextUp }: {
  tasks: TaskItem[];
  now: Date;
  nextUp: TaskItem | null;
}) {
  const active = tasks.filter(t => !t.completed && t.state !== 'Expired');
  const doneToday = tasks.filter(t => t.completed && isDoneToday(t, now));
  const todays = active.filter(t => isToday(t, now));
  const countdown = useCountdown(nextUp, now);

  return (
    <div className="view view-flow">
      <header className="dash-header">
        <div>
          <h1 className="dash-greeting">{greeting(now)}</h1>
          <p className="muted dash-date">
            {now.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric' })}
          </p>
        </div>
        <time className="dash-clock">{now.toLocaleTimeString()}</time>
      </header>

      <div className="stat-row">
        <div className="stat-tile">
          <div className="stat-badge amber" aria-hidden>
            <Icon name="bell-ring" size={18} />
          </div>
          <div>
            <p className="stat-value">{active.length}</p>
            <p className="stat-label">Active Tasks</p>
          </div>
        </div>
        <div className="stat-tile">
          <div className="stat-badge violet" aria-hidden>
            <Icon name="circle-check" size={18} />
          </div>
          <div>
            <p className="stat-value">{doneToday.length}</p>
            <p className="stat-label">Done Today</p>
          </div>
        </div>
      </div>

      <section className="nextup-card" aria-label="Next up">
        {nextUp != null ? (
          <>
            <p className="nextup-kicker">
              <Icon name="calendar-check-2" size={14} /> NEXT UP
            </p>
            <h2 className="nextup-title">{nextUp.title}</h2>
            <p className="nextup-when">{whenLabel(nextUp, now)}</p>
            <p className="nextup-countdown" aria-live="off">{countdown}</p>
          </>
        ) : (
          <>
            <p className="nextup-kicker">
              <Icon name="calendar-check-2" size={14} /> NEXT UP
            </p>
            <p className="nextup-title muted">Nothing scheduled</p>
            <p className="muted">Create a task to see it here.</p>
          </>
        )}
      </section>

      <div className="dash-columns">
        <TaskDigest title="TODAY" tasks={todays} now={now} empty="No tasks today." />
        <TaskDigest title="UPCOMING" tasks={upcomingDigest(active, now)} now={now} empty="Nothing upcoming." />
      </div>
    </div>
  );
}

function TaskDigest({ title, tasks, now, empty }: {
  title: string;
  tasks: TaskItem[];
  now: Date;
  empty: string;
}) {
  return (
    <section className="dash-digest">
      <h3 className="section-label">{title}</h3>
      {tasks.length === 0 ? (
        <p className="muted digest-empty">{empty}</p>
      ) : (
        <ul className="digest-list">
          {tasks.map((task) => (
            <li key={task.id} className="digest-item">
              <span className={`digest-dot ${task.type.toLowerCase()}`} aria-hidden />
              <span className="digest-title">{task.title}</span>
              <span className="digest-when">{whenLabel(task, now)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function useCountdown(nextUp: TaskItem | null, now: Date): string {
  if (nextUp == null) return '';
  const fire = taskDateTime(nextUp);
  if (fire == null) return '';
  const ms = fire.getTime() - now.getTime();
  if (ms <= 0) return 'now';
  const totalMinutes = Math.floor(ms / 60_000);
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  const minutes = totalMinutes % 60;
  const seconds = Math.floor((ms % 60_000) / 1000);
  if (days > 0) return `${days}d ${hours}h`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function greeting(now: Date): string {
  const hour = now.getHours();
  if (hour < 5) return 'Burning the midnight oil';
  if (hour < 12) return 'Good morning';
  if (hour < 18) return 'Good afternoon';
  return 'Good evening';
}

function isToday(task: TaskItem, now: Date): boolean {
  const fire = taskDateTime(task);
  return fire != null && fire.toDateString() === now.toDateString();
}

function isDoneToday(task: TaskItem, now: Date): boolean {
  return isToday(task, now) && task.completed;
}

function upcomingDigest(active: TaskItem[], now: Date): TaskItem[] {
  return active
    .filter(t => {
      const fire = taskDateTime(t);
      return fire != null && fire.getTime() > now.getTime() && fire.toDateString() !== now.toDateString();
    })
    .slice(0, 5);
}
