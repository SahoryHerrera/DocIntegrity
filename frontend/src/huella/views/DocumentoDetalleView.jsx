import { Icon } from '../components/Icon.jsx';
import { EmptyState, PageHead } from '../components/ui.jsx';

/* Detalle de un documento. GET /api/documents/{id} se conecta en la etapa 3. */
export default function DocumentoDetalleView() {
  return (
    <>
      <PageHead title="Documento" back={<a className="back" href="#/documentos"><Icon name="back" />Documentos</a>} />
      <section className="panel">
        <EmptyState icon="file" title="Sin datos todavía" text="El detalle del documento se cargará desde la API de DocIntegrity." />
      </section>
    </>
  );
}
