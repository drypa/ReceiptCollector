import {Component, Inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from "@angular/material/dialog";

@Component({
  selector: 'app-request-result',
  templateUrl: './request-result.component.html',
  styleUrls: ['./request-result.component.scss']
})
export class RequestResultComponent {

  constructor(private dialogRef: MatDialogRef<RequestResultComponent>,
              @Inject(MAT_DIALOG_DATA) public message: string) {
  }

  onClose(): void {
    this.dialogRef.close();
  }

}
