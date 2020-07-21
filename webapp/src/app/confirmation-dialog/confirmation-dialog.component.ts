import { Component, Inject, OnInit } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogRef } from "@angular/material/dialog";
import { ConfirmationDialogData } from "./confirmation-dialog-data";

@Component({
  selector: 'app-confirmation-dialog',
  templateUrl: './confirmation-dialog.component.html',
  styleUrls: ['./confirmation-dialog.component.scss']
})
export class ConfirmationDialogComponent implements OnInit {
  static DIALOG_RESULT_OK: string = 'OK';
  static DIALOG_RESULT_CANCEL: string = 'CANCEL';

  constructor(private dialogRef: MatDialogRef<ConfirmationDialogComponent>,
              @Inject(MAT_DIALOG_DATA) public data: ConfirmationDialogData) {
  }

  ngOnInit() {
  }

  onOk() {
    this.dialogRef.close(ConfirmationDialogComponent.DIALOG_RESULT_OK);
  }

  onCancel() {
    this.dialogRef.close(ConfirmationDialogComponent.DIALOG_RESULT_CANCEL);
  }
}
