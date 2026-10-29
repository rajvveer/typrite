import { useEffect, useState, type ReactNode } from "react";
import { clearSession, getSession, subscribe, type Session } from "./auth";
import { navigate, useRoute, type Route } from "./router";
import { AuditPage } from "./pages/AuditPage";
import { CampaignsPage } from "./pages/CampaignsPage";
import { DevicesPage } from "./pages/DevicesPage";
import { LoginPage } from "./pages/LoginPage";
import { ReleasesPage } from "./pages/ReleasesPage";
import { ReportsPage } from "./pages/ReportsPage";

const NAV: ReadonlyArray<{ route: Route; label: string }> = [
  { route: "devices", label: "Devices" },
  { route: "campaigns", label: "Campaigns" },
  { route: "releases", label: "Releases" },
  { route: "reports", label: "Reports" },
  { route: "audit", label: "Audit" },
];

function useSession(): Session | null {
  const [session, setSession] = useState<Session | null>(getSession);
  useEffect(() => subscribe(setSession), []);
  return session;
}

function Page({ route }: { route: Route }): ReactNode {
  switch (route) {
    case "login":
      return <LoginPage />;
    case "devices":
      return <DevicesPage />;
    case "campaigns":
      return <CampaignsPage />;
    case "releases":
      return <ReleasesPage />;
    case "reports":
      return <ReportsPage />;
    case "audit":
      return <AuditPage />;
  }
}

export function App(): ReactNode {
  const session = useSession();
  const route = useRoute();

  useEffect(() => {
    if (session === null && route !== "login") navigate("login");
    if (session !== null && route === "login") navigate("devices");
  }, [session, route]);

  if (session === null) {
    return (
      <main className="container">
        <LoginPage />
      </main>
    );
  }

  return (
    <>
      <header className="topbar">
        <span className="brand">Typrite Operations</span>
        <nav aria-label="Main">
          {NAV.map((item) => (
            <a
              key={item.route}
              href={`#/${item.route}`}
              aria-current={route === item.route ? "page" : undefined}
            >
              {item.label}
            </a>
          ))}
        </nav>
        <div className="session">
          <span>
            {session.subject.length > 0 ? `${session.subject} · ` : ""}
            <strong>{session.role}</strong>
          </span>
          <button type="button" onClick={clearSession}>
            Sign out
          </button>
        </div>
      </header>
      <main className="container">
        <Page route={route === "login" ? "devices" : route} />
      </main>
    </>
  );
}
