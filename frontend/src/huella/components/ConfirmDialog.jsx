import Modal from './Modal.jsx';

/* Confirmación con el mismo aspecto que los diálogos del prototipo. */
export default function ConfirmDialog({ title, text, confirm, tone = 'primary', onConfirm, onClose }) {
  return (
    <Modal
      title={title} size="sm" onClose={onClose}
      footer={<>
        <button className="btn btn-ghost" type="button" onClick={onClose}>Cancelar</button>
        <button className={`btn btn-${tone}`} type="button" onClick={() => { onClose(); onConfirm(); }}>{confirm}</button>
      </>}
    >
      <p>{text}</p>
    </Modal>
  );
}
