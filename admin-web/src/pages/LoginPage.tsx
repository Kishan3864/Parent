import { useId, useState } from 'react';
import type { FormEvent } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ErrorNote } from '../components/Spinner';

export default function LoginPage() {
  const { isReady, isAuthenticated, login } = useAuth();
  const navigate = useNavigate();
  const emailId = useId();
  const passwordId = useId();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<unknown>(null);
  const [isPending, setIsPending] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    setIsPending(true);
    setError(null);
    try {
      await login(email.trim(), password);
      navigate('/', { replace: true });
    } catch (caught) {
      setError(caught);
      setIsPending(false);
    }
  }

  if (isReady && isAuthenticated) {
    return <Navigate to="/" replace />;
  }

  return (
    <div className="login-page">
      <div className="login-card">
        <h1>ParentalTrack</h1>
        <p className="login-card__sub">Sign in to see where your children&apos;s devices are.</p>

        <form className="login-form" onSubmit={(event) => void handleSubmit(event)}>
          <div className="field">
            <label htmlFor={emailId}>Email</label>
            <input
              id={emailId}
              type="email"
              name="email"
              autoComplete="username"
              required
              value={email}
              disabled={isPending}
              onChange={(event) => setEmail(event.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor={passwordId}>Password</label>
            <input
              id={passwordId}
              type="password"
              name="password"
              autoComplete="current-password"
              required
              value={password}
              disabled={isPending}
              onChange={(event) => setPassword(event.target.value)}
            />
          </div>

          <ErrorNote error={error} />

          <button type="submit" className="btn btn--primary" disabled={isPending}>
            {isPending ? 'Signing in...' : 'Sign in'}
          </button>
        </form>

        <p className="hint login-card__seed">
          In a development environment the API seeds one parent account from its{' '}
          <code>Seed</code> configuration section - <code>Seed:ParentEmail</code> and{' '}
          <code>Seed:ParentPassword</code> in the backend&apos;s{' '}
          <code>appsettings.Development.json</code>.
        </p>
      </div>
    </div>
  );
}
