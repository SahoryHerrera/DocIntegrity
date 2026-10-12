import { Icon } from './Icon.jsx';

/* Servicios de la arquitectura. Su estado real necesita un endpoint de salud (pendiente). */
const SERVICES = [
  { name: 'API REST', desc: 'ASP.NET Core, DocIntegrity.Api', ic: 'server' },
  { name: 'Base de datos', desc: 'PostgreSQL con Entity Framework Core', ic: 'database' },
  { name: 'Modelo de IA local', desc: 'Ollama', ic: 'spark' },
  { name: 'Red blockchain', desc: 'Hardhat, contrato DocumentRegistry', ic: 'blocks' },
  { name: 'Google Identity', desc: 'OAuth 2.0 y OpenID Connect', ic: 'key' },
];

export default function ServiceList() {
  return (
    <ul className="svc-list">
      {SERVICES.map((s) => (
        <li key={s.name}>
          <span className="svc-ic"><Icon name={s.ic} /></span>
          <span className="svc-text"><strong>{s.name}</strong><span>{s.desc}</span></span>
          <span className="svc-state">Sin comprobar</span>
        </li>
      ))}
    </ul>
  );
}
