import React from 'react';

interface CustomDialogProps {
  isOpen: boolean;
  title?: string;
  message: string;
  onClose: () => void;
  onConfirm?: () => void;
  confirmText?: string;
  cancelText?: string;
}

export const CustomDialog: React.FC<CustomDialogProps> = ({
  isOpen,
  title = 'Уведомление',
  message,
  onClose,
 onConfirm,
  confirmText = 'Ок',
  cancelText = 'Отмена'
}) => {
  if (!isOpen) {
    return null;
  }

  return (
    <div className="dialog-overlay">
      <div className="dialog-box">
        {title && <h3 className="dialog-title">{title}</h3>}
        <p className="dialog-message">{message}</p>
        <div className="dialog-buttons">
          {onConfirm ? (
            <>
              <button className="dialog-confirm-btn" onClick={onConfirm}>
                {confirmText}
              </button>
              <button className="dialog-cancel-btn" onClick={onClose}>
                {cancelText}
              </button>
            </>
          ) : (
            <button className="dialog-ok-btn" onClick={onClose}>
              {confirmText}
            </button>
          )}
        </div>
      </div>
    </div>
  );
};