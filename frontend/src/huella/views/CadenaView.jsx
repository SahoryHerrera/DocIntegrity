import Glyph from '../components/Glyph.jsx';
import { Icon } from '../components/Icon.jsx';
import { ChainHistoryNote, LoadError, Loading } from '../components/docs.jsx';
import { CopyButton, EmptyState, HexGroups, Kv, PageHead, Soon } from '../components/ui.jsx';
import { CHAIN, CONTRACT_FUNCTIONS } from '../config.js';
import { useDocuments } from '../hooks/useDocuments.js';
import { docTitle, hasTx } from '../lib/docs.js';
import { fmtDate, fmtNum, short } from '../lib/format.js';

/*
 * Blockchain. Las transacciones son las que PostgreSQL guardó al registrar
 * (datos históricos); el explorador de bloques necesita endpoints que aún no existen.
 */
export default function CadenaView() {
  const docs = useDocuments();
  const ready = docs.status === 'ready' || docs.items.length > 0;
  const txs = docs.items.filter(hasTx).sort((a, b) => new Date(b.registeredAt) - new Date(a.registeredAt));
  const distinct = new Set(txs.map((d) => d.blockchainTransactionHash)).size;

  return (
    <>
      <PageHead
        title="Blockchain"
        lede="Cada registro es una transacción del contrato inteligente. Cada bloque guarda el hash del anterior, así que alterar uno rompe la cadena desde ese punto."
        actions={<button className="btn btn-secondary" type="button" disabled title="Requiere un endpoint que consulte los bloques de la red."><Icon name="shield" />Comprobar la cadena</button>}
      />
      <section className="panel contract">
        <div className="contract-grid">
          <Kv rows={[
            ['Contrato', CHAIN.contractAddress
              ? <span className="hash"><HexGroups hex={CHAIN.contractAddress} /><CopyButton value={CHAIN.contractAddress} msg="Dirección copiada" label="Copiar dirección del contrato" /></span>
              : <span className="muted">No configurado (VITE_CONTRACT_ADDRESS)</span>],
            ['Red', CHAIN.network],
          ]} />
          <Kv rows={[
            ['Transacciones guardadas', ready ? `${fmtNum(txs.length)}${distinct !== txs.length ? ` (${fmtNum(distinct)} distintas)` : ''}` : '—'],
            ['Registros sin transacción', ready ? fmtNum(docs.items.length - txs.length) : '—'],
          ]} />
        </div>
        <div className="fn-list">
          {CONTRACT_FUNCTIONS.map(([sig, desc]) => <div className="fn" key={sig}><code>{sig}</code><span>{desc}</span></div>)}
        </div>
      </section>
      {docs.status === 'error' && <LoadError error={docs.error} onRetry={docs.reload} />}
      <section className="panel">
        <header className="panel-head"><h2>Transacciones guardadas en PostgreSQL</h2></header>
        {!ready ? (docs.status === 'error' ? <p className="muted">Sin datos.</p> : <Loading text="Cargando transacciones…" />)
          : !txs.length ? <EmptyState icon="blocks" title="Aún no hay transacciones" text="Cuando registres un documento, su transacción aparecerá aquí." />
          : (
            <>
              <div className="table-wrap"><table className="table table-compact">
                <thead><tr><th>Bloque</th><th>Transacción</th><th>Documento</th><th className="hide-sm">Registrado</th></tr></thead>
                <tbody>{txs.map((d) => (
                  <tr key={d.id}>
                    <td className="tnum">{fmtNum(d.blockchainBlockNumber)}</td>
                    <td className="mono small">{short(d.blockchainTransactionHash, 10, 6)}</td>
                    <td><a className="cell-doc" href={`#/documentos/${d.id}`}><Glyph hex={d.sha256} size={26} /><span className="cell-title">{docTitle(d)}</span></a></td>
                    <td className="hide-sm muted">{fmtDate(d.registeredAt)}</td>
                  </tr>
                ))}</tbody>
              </table></div>
              <div className="panel-foot"><ChainHistoryNote /></div>
            </>
          )}
      </section>
      <section className="panel demo-panel">
        <header className="panel-head"><h2>Explorador de bloques</h2><Soon title="Requiere endpoints de consulta de bloques en el backend." /></header>
        <p className="muted">Recorrer los bloques de la red activa, comprobar los enlaces entre ellos y simular una alteración necesita que el backend exponga la consulta de bloques.</p>
      </section>
    </>
  );
}
