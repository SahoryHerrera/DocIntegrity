import { Icon } from '../components/Icon.jsx';
import { EmptyState, Kv, PageHead, Soon } from '../components/ui.jsx';
import { CONTRACT_FUNCTIONS } from '../config.js';

/*
 * Blockchain. Las transacciones reales (hash y bloque de cada documento) llegan
 * en la etapa 3; el explorador de bloques necesita endpoints que aún no existen.
 */
export default function CadenaView() {
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
            ['Contrato', <span className="muted">Se configurará con las variables de entorno</span>],
            ['Red', <span className="muted">Hardhat local</span>],
          ]} />
          <Kv rows={[
            ['Transacciones', '—'],
            ['Último bloque', '—'],
          ]} />
        </div>
        <div className="fn-list">
          {CONTRACT_FUNCTIONS.map(([sig, desc]) => <div className="fn" key={sig}><code>{sig}</code><span>{desc}</span></div>)}
        </div>
      </section>
      <section className="panel">
        <header className="panel-head"><h2>Transacciones registradas</h2></header>
        <EmptyState icon="blocks" title="Sin datos todavía" text="La transacción y el bloque de cada documento se cargarán desde la API de DocIntegrity." />
      </section>
      <section className="panel demo-panel">
        <header className="panel-head"><h2>Explorador de bloques</h2><Soon title="Requiere endpoints de consulta de bloques en el backend." /></header>
        <p className="muted">Recorrer los bloques, comprobar los enlaces entre ellos y simular una alteración necesita que el backend exponga la consulta de bloques de la red.</p>
      </section>
    </>
  );
}
